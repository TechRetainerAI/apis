using System.ComponentModel.DataAnnotations;
using MeDan.Api.Models;

namespace MeDan.Api.Dtos;

/// <summary>Start a Paystack transaction for a booking. The payer is taken from the token.</summary>
public record InitializePaymentRequest
{
    [Required] public Guid BookingId { get; init; }

    /// <summary>"momoMtn" | "momoTelecel" | "card".</summary>
    public PaymentChannel Channel { get; init; } = PaymentChannel.MomoMtn;

    /// <summary>MoMo number. Falls back to the user's saved phone.</summary>
    [MaxLength(30)] public string? Phone { get; init; }
}

/// <summary>A payment attempt as the app sees it (mirrors Dart <c>PaymentIntent</c>).</summary>
public record PaymentResponse
{
    public string Reference { get; init; } = default!;
    public Guid BookingId { get; init; }

    /// <summary>Amount in GH₵ (not pesewas).</summary>
    public int Amount { get; init; }

    public string Channel { get; init; } = default!;
    public string Status { get; init; } = default!;
    public string? CheckoutUrl { get; init; }
    public string? AuthorizationCode { get; init; }

    /// <summary>The booking's state after this payment was applied, e.g. "paymentHeld".</summary>
    public string BookingStatus { get; init; } = default!;

    public DateTime CreatedAt { get; init; }

    /// <summary>True when no Paystack key is configured and the transaction was simulated.</summary>
    public bool Simulated { get; init; }

    /// <summary>
    /// Paystack is holding this Mobile Money charge until the customer submits
    /// the code they were sent. The app must collect it and POST it to
    /// <c>/api/payments/{reference}/submit-otp</c>; polling alone will never
    /// resolve.
    /// </summary>
    public bool RequiresOtp { get; init; }

    /// <summary>Paystack's own instruction to show the customer, when it sends one.</summary>
    public string? DisplayText { get; init; }

    // ------------------------------------------------- manual MoMo (optional)

    /// <summary>
    /// Where to fetch the uploaded proof, e.g. "/api/payments/MD-M-ab12/proof".
    /// Null unless this is a manual payment. Requires the caller's bearer token —
    /// it is not a public image URL.
    /// </summary>
    public string? ProofUrl { get; init; }

    public string? SenderPhone { get; init; }
    public string? SenderName { get; init; }
    public string? ProviderTransactionId { get; init; }
    public DateTime? SubmittedAt { get; init; }
    public DateTime? ReviewedAt { get; init; }

    /// <summary>Why staff rejected the proof, when they did.</summary>
    public string? ReviewNote { get; init; }
}

/// <summary>Where to send a manual transfer, for the "pay by MoMo" screen.</summary>
public record ManualPaymentInstructionsResponse
{
    public bool Enabled { get; init; }

    /// <summary>e.g. "0559960788".</summary>
    public string WalletNumber { get; init; } = default!;

    /// <summary>e.g. "CY TECHNOLOGIES AND CONSULTING".</summary>
    public string WalletName { get; init; } = default!;

    /// <summary>e.g. "MoMo wallet".</summary>
    public string WalletType { get; init; } = default!;

    public string? Instructions { get; init; }

    /// <summary>What this particular booking costs, GH₵.</summary>
    public int Amount { get; init; }
}

/// <summary>
/// A completed manual transfer plus its evidence. Sent as multipart/form-data
/// because of <see cref="Proof"/>.
/// </summary>
public record SubmitManualPaymentRequest
{
    [Required] public Guid BookingId { get; init; }

    /// <summary>Screenshot of the transfer. JPG, PNG or WEBP, up to 5 MB.</summary>
    [Required] public IFormFile Proof { get; init; } = default!;

    /// <summary>Transaction ID on the student's receipt.</summary>
    [MaxLength(100)] public string? TransactionId { get; init; }

    /// <summary>The number the money was sent from. Falls back to the user's saved phone.</summary>
    [MaxLength(30)] public string? SenderPhone { get; init; }

    /// <summary>Wallet name on the receipt. Falls back to the user's name.</summary>
    [MaxLength(150)] public string? SenderName { get; init; }
}

/// <summary>A submission in the staff review queue, with enough context to decide.</summary>
public record ManualPaymentReviewResponse
{
    public string Reference { get; init; } = default!;
    public Guid BookingId { get; init; }
    public int Amount { get; init; }
    public string Status { get; init; } = default!;

    public Guid StudentUserId { get; init; }
    public string StudentName { get; init; } = default!;
    public string StudentEmail { get; init; } = default!;

    public string HostelName { get; init; } = default!;

    public string? SenderPhone { get; init; }
    public string? SenderName { get; init; }
    public string? ProviderTransactionId { get; init; }

    /// <summary>Authorized endpoint for the screenshot.</summary>
    public string? ProofUrl { get; init; }

    public DateTime? SubmittedAt { get; init; }
    public DateTime? ReviewedAt { get; init; }
    public string? ReviewNote { get; init; }

    /// <summary>
    /// Other submissions quoting this same transaction ID. Non-empty means someone
    /// is reusing a receipt — the thing manual review exists to catch.
    /// </summary>
    public IEnumerable<string> DuplicateOf { get; init; } = Array.Empty<string>();
}

/// <summary>Why staff turned a manual payment down. The student is shown this.</summary>
public record RejectManualPaymentRequest
{
    [Required]
    [MaxLength(500)]
    public string Reason { get; init; } = default!;
}

/// <summary>The code the customer received for a Mobile Money charge.</summary>
public record SubmitOtpRequest
{
    public string Otp { get; init; } = default!;
}
