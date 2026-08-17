using System.ComponentModel.DataAnnotations;

namespace MeDan.Api.Models;

/// <summary>
/// A student's reservation of a single bed, with escrow lifecycle (see <see cref="BookingStatus"/>).
/// </summary>
public class Booking
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid StudentUserId { get; set; }
    public AppUser Student { get; set; } = default!;

    public Guid HostelId { get; set; }
    public Hostel Hostel { get; set; } = default!;

    public Guid RoomId { get; set; }
    public Room Room { get; set; } = default!;

    public Guid BedId { get; set; }
    public Bed Bed { get; set; } = default!;

    /// <summary>Denormalized for fast company/owner dashboards.</summary>
    public Guid CompanyId { get; set; }

    [MaxLength(20)]
    public string AcademicYear { get; set; } = default!;   // e.g. "2025/2026"

    /// <summary>Total paid by the student, GH₵.</summary>
    public int Amount { get; set; }

    /// <summary>Platform commission withheld, GH₵.</summary>
    public int Commission { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Pending;

    [MaxLength(20)]
    public string? CheckInCode { get; set; }

    [MaxLength(100)]
    public string? PaystackReference { get; set; }

    /// <summary>Why the student raised a dispute inside the 48h window.</summary>
    [MaxLength(1000)]
    public string? DisputeReason { get; set; }

    /// <summary>How support closed the dispute (refunded / released to the owner).</summary>
    [MaxLength(1000)]
    public string? DisputeResolution { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? DisputedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }

    public Payment? Payment { get; set; }
}
