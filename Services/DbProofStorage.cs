using MeDan.Api.Data;
using MeDan.Api.Models;

namespace MeDan.Api.Services;

/// <summary>
/// Keeps payment proof screenshots in the database as private
/// <see cref="StoredImage"/> rows. Replaces <see cref="LocalProofStorage"/>:
/// a proof wiped by a redeploy is the lost evidence behind a transfer staff
/// may already have approved, which is the one upload that must never die
/// with the container. The public images endpoint refuses private rows —
/// these remain readable only through /api/payments/{reference}/proof.
/// </summary>
public class DbProofStorage : IProofStorage
{
    private const long MaxImageBytes = 5 * 1024 * 1024;  // 5 MB
    // A screen recording of the MoMo app is a legitimate proof too; recordings
    // run bigger than screenshots, so they get their own ceiling.
    private const long MaxVideoBytes = 25 * 1024 * 1024; // 25 MB

    private static readonly HashSet<string> ImageContentTypes = new(StringComparer.OrdinalIgnoreCase)
        { "image/jpeg", "image/png", "image/webp" };

    // mp4 + 3gp cover what Ghanaian Android phones record; quicktime is iPhone.
    private static readonly HashSet<string> VideoContentTypes = new(StringComparer.OrdinalIgnoreCase)
        { "video/mp4", "video/quicktime", "video/webm", "video/3gpp" };

    private readonly AppDbContext _db;

    public DbProofStorage(AppDbContext db) => _db = db;

    public async Task<string> SaveAsync(IFormFile file, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            throw new InvalidImageException("No proof was uploaded.");

        var isVideo = VideoContentTypes.Contains(file.ContentType);
        if (!isVideo && !ImageContentTypes.Contains(file.ContentType))
            throw new InvalidImageException(
                "The proof must be a JPG, PNG or WEBP image, or an MP4, MOV, WEBM or 3GP video.");

        var maxBytes = isVideo ? MaxVideoBytes : MaxImageBytes;
        if (file.Length > maxBytes)
            throw new InvalidImageException(
                $"The {(isVideo ? "video" : "screenshot")} is too large (max {maxBytes / (1024 * 1024)} MB).");

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);

        var image = new StoredImage
        {
            ContentType = file.ContentType,
            Data = buffer.ToArray(),
            IsPublic = false
        };

        _db.StoredImages.Add(image);
        await _db.SaveChangesAsync(ct);

        return image.Id.ToString("N");
    }

    public ProofFile? Open(string? key)
    {
        // Legacy keys were disk paths; those files are gone on the deployed
        // host, so an unparsable key simply has no proof any more.
        if (!Guid.TryParse(key, out var id)) return null;

        var row = _db.StoredImages.Find(id);
        if (row is null || row.IsPublic) return null;

        var ext = row.ContentType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            "video/mp4" => ".mp4",
            "video/quicktime" => ".mov",
            "video/webm" => ".webm",
            "video/3gpp" => ".3gp",
            _ => ".jpg"
        };
        return new ProofFile(new MemoryStream(row.Data), row.ContentType, $"proof-{id:N}{ext}");
    }

    public void Delete(string? key)
    {
        if (!Guid.TryParse(key, out var id)) return;
        var row = _db.StoredImages.Find(id);
        if (row is null || row.IsPublic) return;
        _db.StoredImages.Remove(row);
        _db.SaveChanges();
    }
}
