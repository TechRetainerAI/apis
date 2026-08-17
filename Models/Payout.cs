namespace MeDan.Api.Models;

/// <summary>Where an outbound payment stands with Paystack.</summary>
public enum PayoutStatus
{
    /// <summary>Recorded locally, not yet sent to Paystack.</summary>
    Pending = 0,

    /// <summary>Accepted by Paystack and in flight.</summary>
    Processing = 1,

    /// <summary>Money reached the recipient.</summary>
    Paid = 2,

    /// <summary>Paystack rejected or reversed it — see <see cref="Payout.FailureReason"/>.</summary>
    Failed = 3,

    /// <summary>Money returned to the student instead of released to the owner.</summary>
    Refunded = 4
}

/// <summary>What the money movement was for.</summary>
public enum PayoutKind
{
    /// <summary>Escrow released to the hostel owner after the dispute window.</summary>
    Release = 0,

    /// <summary>Booking refunded to the student.</summary>
    Refund = 1
}

/// <summary>
/// One outbound money movement for a booking — the counterpart to <see cref="Payment"/>,
/// which only records money coming in.
///
/// A booking has at most one payout per <see cref="PayoutKind"/>; that pair is unique in
/// the database, which is what makes a release idempotent. A retry finds the existing row
/// rather than paying twice.
/// </summary>
public class Payout
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BookingId { get; set; }
    public Booking Booking { get; set; } = default!;

    /// <summary>Who is being paid — null for a refund, which goes back to the student.</summary>
    public Guid? CompanyId { get; set; }

    public PayoutKind Kind { get; set; }

    /// <summary>GH₵ actually sent: the booking amount less MeDan's commission on a release,
    /// or the full amount on a refund.</summary>
    public int Amount { get; set; }

    /// <summary>
    /// Our own idempotency key, also sent to Paystack as the transfer reference so a
    /// duplicate request is rejected at their end too.
    /// </summary>
    public string Reference { get; set; } = default!;

    public PayoutStatus Status { get; set; } = PayoutStatus.Pending;

    /// <summary>Paystack's transfer code ("TRF_...") or refund id, once issued.</summary>
    public string? ProviderReference { get; set; }

    /// <summary>Why it failed, verbatim from Paystack, so support can act on it.</summary>
    public string? FailureReason { get; set; }

    /// <summary>How many times sending has been attempted.</summary>
    public int Attempts { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SettledAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
}
