using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MeDan.Api.Data;

/// <summary>
/// Adds columns and indexes that the model expects but the Postgres database does not have.
///
/// Why this exists: the committed migrations are SQL Server-specific, so Postgres builds its
/// schema with <c>EnsureCreated()</c> instead. That call creates the schema once and then
/// ignores the model forever — on a database that already exists it is a no-op, including
/// when a release adds a property. EF still writes every mapped column into its SELECTs and
/// INSERTs, so the first deploy after a model change turns the whole table into an error:
/// <c>42703: column p.ProofKey does not exist</c>, on reads as well as writes.
///
/// This closes that gap conservatively. It only ever adds what is missing — nothing is
/// dropped, altered, renamed or backfilled — and it skips non-nullable columns, which cannot
/// be added to a populated table without a default, logging them for a human instead.
///
/// It is a safety net, not a migration system. Renames, type changes and data moves still
/// need real migrations; the durable fix is a Postgres migration set and
/// <c>Database.Migrate()</c>.
/// </summary>
public static class PostgresSchemaSync
{
    public static async Task ApplyAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        var existing = await ReadExistingColumnsAsync(db, ct);
        if (existing.Count == 0)
        {
            // A brand new database EnsureCreated() just built, or no permission to read
            // the catalog. Either way there is nothing safe to reconcile against.
            logger.LogDebug("Schema sync: no catalog rows read; skipping.");
            return;
        }

        // Whole tables first — a release that adds an entity leaves EnsureCreated()
        // just as silent as a new column does, and every query against the new type
        // fails with "relation does not exist" until the table is there.
        if (await CreateMissingTablesAsync(db, logger, existing, ct))
            existing = await ReadExistingColumnsAsync(db, ct);

        var added = 0;
        var skipped = new List<string>();

        foreach (var entity in db.Model.GetEntityTypes())
        {
            var table = entity.GetTableName();
            if (string.IsNullOrEmpty(table)) continue;

            var store = StoreObjectIdentifier.Table(table, entity.GetSchema());

            foreach (var property in entity.GetProperties())
            {
                var column = property.GetColumnName(store);
                if (string.IsNullOrEmpty(column)) continue;
                if (existing.Contains((table, column))) continue;

                // Adding NOT NULL to a table with rows needs a default we cannot invent.
                if (!property.IsNullable)
                {
                    skipped.Add($"{table}.{column}");
                    continue;
                }

                var type = property.GetColumnType(store);
                if (string.IsNullOrWhiteSpace(type))
                {
                    skipped.Add($"{table}.{column} (no column type)");
                    continue;
                }

                var sql =
                    $"""ALTER TABLE "{table}" ADD COLUMN IF NOT EXISTS "{column}" {type}""";

                await db.Database.ExecuteSqlRawAsync(sql, ct);
                logger.LogWarning(
                    "Schema sync: added missing column {Table}.{Column} ({Type}).",
                    table, column, type);
                added++;
            }
        }

        // Indexes only after the columns they cover exist.
        foreach (var entity in db.Model.GetEntityTypes())
        {
            var table = entity.GetTableName();
            if (string.IsNullOrEmpty(table)) continue;

            var store = StoreObjectIdentifier.Table(table, entity.GetSchema());

            foreach (var index in entity.GetIndexes())
            {
                var name = index.GetDatabaseName(store);
                if (string.IsNullOrEmpty(name)) continue;

                var columns = index.Properties
                    .Select(p => p.GetColumnName(store))
                    .Where(c => !string.IsNullOrEmpty(c))
                    .ToList();

                if (columns.Count != index.Properties.Count) continue;

                var unique = index.IsUnique ? "UNIQUE " : "";
                var cols = string.Join(", ", columns.Select(c => $"\"{c}\""));
                var filter = index.GetFilter(store);
                var where = string.IsNullOrWhiteSpace(filter) ? "" : $" WHERE {filter}";

                // Indexes the database already has are the common case and IF NOT EXISTS
                // makes those free. A filter authored for another provider could still be
                // rejected here, so one bad index must not cost us the rest.
                try
                {
                    await db.Database.ExecuteSqlRawAsync(
                        $"""CREATE {unique}INDEX IF NOT EXISTS "{name}" ON "{table}" ({cols}){where}""",
                        ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Schema sync: could not create index {Index}.", name);
                }
            }
        }

        if (added > 0)
            logger.LogWarning(
                "Schema sync added {Count} column(s). The model had drifted ahead of the " +
                "database — see PostgresSchemaSync.", added);

        if (skipped.Count > 0)
            logger.LogError(
                "Schema sync could not add required (NOT NULL) column(s): {Columns}. " +
                "These need a migration or a manual ALTER with a default.",
                string.Join(", ", skipped));
    }

    /// <summary>
    /// Creates tables the model declares but the database lacks, taking the DDL from EF's
    /// own create script so the column types, keys and constraints match exactly what
    /// EnsureCreated() would have built. Only the CREATE TABLE blocks are executed:
    /// indexes are left to the index pass, and the script's seed-data INSERTs are skipped
    /// (their text could contain the semicolons this splits on).
    /// </summary>
    /// <returns>True when at least one table was created.</returns>
    private static async Task<bool> CreateMissingTablesAsync(
        AppDbContext db,
        ILogger logger,
        HashSet<(string Table, string Column)> existing,
        CancellationToken ct)
    {
        var present = existing.Select(e => e.Table).ToHashSet(StringComparer.Ordinal);

        var missing = db.Model.GetEntityTypes()
            .Select(e => e.GetTableName())
            .Where(t => !string.IsNullOrEmpty(t) && !present.Contains(t!))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (missing.Count == 0) return false;

        var script = db.Database.GenerateCreateScript();
        var created = 0;

        foreach (var table in missing)
        {
            var ddl = ExtractCreateTable(script, table!);
            if (ddl is null)
            {
                logger.LogError(
                    "Schema sync: table {Table} is missing but no CREATE statement was found for it.",
                    table);
                continue;
            }

            try
            {
                await db.Database.ExecuteSqlRawAsync(ddl, ct);
                logger.LogWarning("Schema sync: created missing table {Table}.", table);
                created++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Schema sync: could not create table {Table}.", table);
            }
        }

        return created > 0;
    }

    /// <summary>
    /// Pulls one <c>CREATE TABLE "name" ( … );</c> block out of an EF create script,
    /// ending at the first line that closes the block rather than at any semicolon,
    /// so a default value containing one cannot truncate it.
    /// </summary>
    private static string? ExtractCreateTable(string script, string table)
    {
        var marker = $"CREATE TABLE \"{table}\" (";
        var start = script.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;

        var lines = script[start..].Split('\n');
        var block = new List<string>();

        foreach (var line in lines)
        {
            block.Add(line.TrimEnd('\r'));
            if (line.TrimEnd('\r', ' ') == ");") return string.Join('\n', block);
        }

        return null;   // unterminated — better to report nothing than half a table
    }

    /// <summary>Every (table, column) pair the database currently has, in the active schema.</summary>
    private static async Task<HashSet<(string Table, string Column)>> ReadExistingColumnsAsync(
        AppDbContext db, CancellationToken ct)
    {
        var found = new HashSet<(string, string)>();

        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed) await connection.OpenAsync(ct);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT table_name, column_name
                FROM information_schema.columns
                WHERE table_schema = current_schema()
                """;

            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                found.Add((reader.GetString(0), reader.GetString(1)));
        }
        finally
        {
            if (wasClosed) await connection.CloseAsync();
        }

        return found;
    }
}
