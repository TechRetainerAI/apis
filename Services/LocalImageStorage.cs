namespace MeDan.Api.Services;

/// <summary>
/// Saves images under the upload root and serves them as static files at
/// <c>/uploads/&lt;subfolder&gt;/…</c>.
///
/// The root defaults to <c>wwwroot/uploads</c> but is overridden with
/// <c>Storage:UploadRoot</c>, which is what a deployment points at a mounted disk.
/// It has to be settable: a container's own filesystem is thrown away on every deploy
/// and restart, so uploads written inside the image survive only until the next one —
/// the database keeps serving URLs whose files are long gone.
///
/// Good for a single server. For scale, swap this implementation for S3/R2/Blob
/// (same interface) so more than one instance can serve the same files.
/// </summary>
public class LocalImageStorage : IImageStorage
{
    private const long MaxBytes = 5 * 1024 * 1024; // 5 MB
    private static readonly HashSet<string> AllowedExt = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
        { "image/jpeg", "image/png", "image/webp", "image/gif" };

    private readonly string _root;

    public LocalImageStorage(IWebHostEnvironment env, IConfiguration config)
    {
        _root = ResolveRoot(env, config);
    }

    /// <summary>
    /// The directory <c>/uploads</c> is served from. Shared with Program.cs, which
    /// registers the static-file provider over the same path — if these two disagree,
    /// every upload 404s.
    /// </summary>
    public static string ResolveRoot(IWebHostEnvironment env, IConfiguration config)
    {
        var configured = config["Storage:UploadRoot"];
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        var webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
        return Path.Combine(webRoot, "uploads");
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

        var folder = Path.Combine(_root, subfolder);
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

        // "/uploads/" is the request path, not a directory under the root.
        var relative = relativeUrl["/uploads/".Length..].Replace('/', Path.DirectorySeparatorChar);
        if (relative.Contains("..")) return;

        var fullPath = Path.Combine(_root, relative);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
    }
}
