namespace MeDan.Api.Services;

/// <summary>
/// Saves images to <c>wwwroot/uploads/&lt;subfolder&gt;</c> on the server's disk and serves them
/// as static files. Good for dev / single-server. For production/scale, swap this implementation
/// for Azure Blob Storage / Amazon S3 / Firebase Storage (same interface).
/// </summary>
public class LocalImageStorage : IImageStorage
{
    private const long MaxBytes = 5 * 1024 * 1024; // 5 MB
    private static readonly HashSet<string> AllowedExt = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
        { "image/jpeg", "image/png", "image/webp", "image/gif" };

    private readonly string _root;

    public LocalImageStorage(IWebHostEnvironment env)
    {
        _root = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
    }

    public async Task<string> SaveAsync(IFormFile file, string subfolder, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            throw new InvalidImageException("No file was uploaded.");
        if (file.Length > MaxBytes)
            throw new InvalidImageException($"File is too large (max {MaxBytes / (1024 * 1024)} MB).");

        var ext = Path.GetExtension(file.FileName);
        if (!AllowedExt.Contains(ext) || !AllowedContentTypes.Contains(file.ContentType))
            throw new InvalidImageException("Only JPG, PNG, WEBP or GIF images are allowed.");

        var folder = Path.Combine(_root, "uploads", subfolder);
        Directory.CreateDirectory(folder);

        var fileName = $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
        var fullPath = Path.Combine(folder, fileName);

        await using (var stream = new FileStream(fullPath, FileMode.Create))
            await file.CopyToAsync(stream, ct);

        // Forward slashes for URLs regardless of OS.
        return $"/uploads/{subfolder}/{fileName}";
    }

    public void Delete(string? relativeUrl)
    {
        if (string.IsNullOrWhiteSpace(relativeUrl) || !relativeUrl.StartsWith("/uploads/"))
            return; // external URL or nothing to do

        var relative = relativeUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.Combine(_root, relative);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
    }
}
