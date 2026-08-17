namespace MeDan.Api.Services;

/// <summary>Bound from the "Paystack" configuration section.</summary>
public class PaystackOptions
{
    public const string SectionName = "Paystack";

    /// <summary>
    /// Live/test secret key (<c>sk_test_…</c> / <c>sk_live_…</c>). Keep it out of source control —
    /// use user-secrets or the <c>Paystack__SecretKey</c> environment variable.
    /// When empty the API runs in <see cref="Simulate"/> mode (Development only).
    /// </summary>
    public string? SecretKey { get; set; }

    public string BaseUrl { get; set; } = "https://api.paystack.co";

    /// <summary>Where Paystack sends the browser after checkout. Optional.</summary>
    public string? CallbackUrl { get; set; }

    /// <summary>Currency code — GHS for Ghana MoMo.</summary>
    public string Currency { get; set; } = "GHS";
}
