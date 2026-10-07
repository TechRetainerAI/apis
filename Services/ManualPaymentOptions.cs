namespace MeDan.Api.Services;

/// <summary>
/// The platform wallet students send to when paying by hand, bound from the
/// <c>ManualPayment</c> configuration section.
///
/// These live in config rather than in the app so the wallet can change without
/// shipping a release — the app asks
/// <c>GET /api/payments/manual/instructions</c> for them.
/// </summary>
public class ManualPaymentOptions
{
    /// <summary>Turn the manual route off to force everyone through Paystack.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The wallet to send to, e.g. "0559960788".</summary>
    public string WalletNumber { get; set; } = "";

    /// <summary>Registered name on the wallet, so the student can check before sending.</summary>
    public string WalletName { get; set; } = "";

    /// <summary>Shown as the payment type, e.g. "MoMo wallet".</summary>
    public string WalletType { get; set; } = "MoMo wallet";

    /// <summary>Optional extra line of guidance under the wallet details.</summary>
    public string? Instructions { get; set; }

    /// <summary>Where <see cref="LocalProofStorage"/> writes. Empty means the default path.</summary>
    public string? ProofRoot { get; set; }

    /// <summary>Configured enough to show a student? A wallet number is the minimum.</summary>
    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(WalletNumber);
}
