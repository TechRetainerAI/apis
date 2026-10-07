namespace MeDan.Api.Services;

/// <summary>
/// Stores payment proof screenshots somewhere the web server will not serve directly.
///
/// Deliberately not <see cref="IImageStorage"/>: that one returns a public
/// <c>/uploads/...</c> URL, which is right for hostel photos and wrong for a MoMo
/// receipt showing a student's name, number and balance. These come back as opaque
/// keys and are only readable through an endpoint that checks the caller.
/// </summary>
public interface IProofStorage
{
    /// <returns>An opaque key to persist on the payment row.</returns>
    Task<string> SaveAsync(IFormFile file, CancellationToken ct = default);

    /// <summary>Opens a stored proof, or null when the key is unknown.</summary>
    ProofFile? Open(string? key);

    /// <summary>Deletes a stored proof. No-op when missing.</summary>
    void Delete(string? key);
}

/// <summary>An open proof image, ready to stream back to an authorized caller.</summary>
public sealed record ProofFile(Stream Content, string ContentType, string FileName) : IDisposable
{
    public void Dispose() => Content.Dispose();
}
