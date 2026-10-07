using System.ComponentModel.DataAnnotations;

namespace MeDan.Api.Models;

/// <summary>A payment attempt against a booking (Paystack: MoMo / card).</summary>
public class Payment
{
    /// <summary>Payment provider reference. Primary key.</summary>
    [MaxLength(100)]
    public string Reference { get; set; } = default!;

    public Guid BookingId { get; set; }
    public Booking Booking { get; set; } = default!;

    public int Amount { get; set; }
    public PaymentChannel Channel { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Initialized;

    [MaxLength(500)]
    public string? CheckoutUrl { get; set; }

    [MaxLength(100)]
    public string? AuthorizationCode { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ---------------------------------------------------------- manual MoMo
    // Only set when Channel is ManualMomo: the student transferred to the
    // platform wallet themselves, so the evidence and the staff decision live
    // here instead of with a provider.

    /// <summary>
    /// Private storage key for the uploaded screenshot (not a public URL — the
    /// image is served by <c>GET /api/payments/{reference}/proof</c>, which
    /// checks the caller first). A receipt shows the student's name, number and
    /// balance, so it is deliberately not in wwwroot.
    /// </summary>
    [MaxLength(200)]
    public string? ProofKey { get; set; }

    /// <summary>Transaction ID from the student's MoMo receipt, used to spot reuse.</summary>
    [MaxLength(100)]
    public string? ProviderTransactionId { get; set; }

    /// <summary>The number the student says they sent from.</summary>
    [MaxLength(30)]
    public string? SenderPhone { get; set; }

    /// <summary>The wallet name on the student's receipt.</summary>
    [MaxLength(150)]
    public string? SenderName { get; set; }

    public DateTime? SubmittedAt { get; set; }

    /// <summary>Staff member who approved or rejected. Null while pending.</summary>
    public Guid? ReviewedByUserId { get; set; }

    public DateTime? ReviewedAt { get; set; }

    /// <summary>Why staff rejected it — shown to the student so they can fix it.</summary>
    [MaxLength(500)]
    public string? ReviewNote { get; set; }
}
