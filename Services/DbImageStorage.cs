using MeDan.Api.Data;
using MeDan.Api.Models;

namespace MeDan.Api.Services;

/// <summary>
/// Stores uploaded images as rows in the database and serves them through
/// <c>GET /api/images/{id}</c>. Replaces <see cref="LocalImageStorage"/> on
/// deployments whose disk does not survive a deploy (Render free tier) —
/// see <see cref="StoredImage"/> for the full rationale.
/// </summary>
public class DbImageStorage : IImageStorage
{
    private const long MaxBytes = 5 * 1024 * 1024; // 5 MB, same as LocalImageStorage
    private static readonly HashSet<string> AllowedExt = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
        { "image/jpeg", "image/png", "image/webp", "image/gif" };

    private readonly AppDbContext _db;

    public DbImageStorage(AppDbContext db) => _db = db;

    public async Task<string> SaveAsync(IFormFile file, string subfolder, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            throw new InvalidImageException("No file was uploaded.");
        if (file.Length > MaxBytes)
            throw new InvalidImageException($"File is too large (max {MaxBytes / (1024 * 1024)} MB).");

        var ext = Path.GetExtension(file.FileName);
        if (!AllowedExt.Contains(ext) || !AllowedContentTypes.Contains(file.ContentType))
            throw new InvalidImageException("Only JPG, PNG, WEBP or GIF images are allowed.");

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);

        // `subfolder` ("hostels", "avatars", …) only mattered for disk layout;
        // rows need no such split.
        var image = new StoredImage
        {
            ContentType = file.ContentType,
            Data = buffer.ToArray(),
            IsPublic = true
        };

        _db.StoredImages.Add(image);
        await _db.SaveChangesAsync(ct);

        return $"/api/images/{image.Id}";
    }

    public void Delete(string? relativeUrl)
    {
        // Legacy "/uploads/..." URLs point at disk files that are already gone
        // on the deployed host; nothing to do for those.
        if (!TryParseId(relativeUrl, out var id)) return;

        var row = _db.StoredImages.Find(id);
        if (row is null) return;
        _db.StoredImages.Remove(row);
        _db.SaveChanges();
    }

    private static bool TryParseId(string? relativeUrl, out Guid id)
    {
        id = default;
        if (string.IsNullOrWhiteSpace(relativeUrl)) return false;
        const string prefix = "/api/images/";
        if (!relativeUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        return Guid.TryParse(relativeUrl[prefix.Length..], out id);
    }
}
