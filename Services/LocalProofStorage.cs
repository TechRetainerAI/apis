namespace MeDan.Api.Services;

/// <summary>
/// Writes proofs to a directory outside <c>wwwroot</c> (default
/// <c>&lt;contentRoot&gt;/storage/payment-proofs</c>, override with
/// <c>ManualPayment:ProofRoot</c>) so no static-file handler can reach them.
///
/// Same caveat as <see cref="LocalImageStorage"/>: this is local disk. On a host with an
/// ephemeral filesystem the files vanish on redeploy, so point ProofRoot at a mounted
/// disk — or swap this implementation for S3/Blob, which the interface is shaped for.
/// </summary>
public class LocalProofStorage : IProofStorage
{
    private const long MaxBytes = 5 * 1024 * 1024; // 5 MB

    private static readonly Dictionary<string, string> AllowedTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = ".jpg",
            ["image/png"] = ".png",
            ["image/webp"] = ".webp"
        };

    private static readonly Dictionary<string, string> TypeByExtension =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".webp"] = "image/webp"
        };

    private readonly string _root;

    public LocalProofStorage(IWebHostEnvironment env, IConfiguration config)
    {
        var configured = config["ManualPayment:ProofRoot"];
        _root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(env.ContentRootPath, "storage", "payment-proofs")
            : configured;
    }

    public async Task<string> SaveAsync(IFormFile file, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            throw new InvalidImageException("Attach a screenshot of the transfer.");
        if (file.Length > MaxBytes)
            throw new InvalidImageException($"That image is too large (max {MaxBytes / (1024 * 1024)} MB).");

        // Trust the declared content type only far enough to pick an extension; the
        // name the client sent is never used to build the path.
        if (!AllowedTypes.TryGetValue(file.ContentType ?? "", out var ext))
            throw new InvalidImageException("Upload the screenshot as a JPG, PNG or WEBP image.");

        Directory.CreateDirectory(_root);

        var key = $"{Guid.NewGuid():N}{ext}";
        await using var stream = new FileStream(Path.Combine(_root, key), FileMode.CreateNew);
        await file.CopyToAsync(stream, ct);

        return key;
    }

    public ProofFile? Open(string? key)
    {
        var path = Resolve(key);
        if (path is null || !File.Exists(path)) return null;

        var contentType = TypeByExtension.GetValueOrDefault(
            Path.GetExtension(path), "application/octet-stream");

        return new ProofFile(
            File.OpenRead(path),
            contentType,
            Path.GetFileName(path));
    }

    public void Delete(string? key)
    {
        var path = Resolve(key);
        if (path is not null && File.Exists(path)) File.Delete(path);
    }

    /// <summary>
    /// Maps a stored key to a path, refusing anything that is not a bare filename —
    /// a key is only ever one we generated, so a separator in it means tampering.
    /// </summary>
    private string? Resolve(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        if (key != Path.GetFileName(key)) return null;
        return Path.Combine(_root, key);
    }
}
