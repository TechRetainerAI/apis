namespace MeDan.Api.Services;

/// <summary>Stores uploaded images and returns a relative URL (e.g. "/uploads/hostels/abc.jpg").</summary>
public interface IImageStorage
{
    /// <param name="subfolder">e.g. "hostels" or "avatars".</param>
    /// <returns>Relative URL the file is served at.</returns>
    Task<string> SaveAsync(IFormFile file, string subfolder, CancellationToken ct = default);

    /// <summary>Deletes a previously stored file given its relative URL. No-op if missing/external.</summary>
    void Delete(string? relativeUrl);
}

/// <summary>Thrown when an uploaded file is rejected (type/size). Surfaced as HTTP 400.</summary>
public class InvalidImageException(string message) : Exception(message);
