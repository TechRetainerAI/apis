using System.ComponentModel.DataAnnotations;
using MeDan.Api.Models;

namespace MeDan.Api.Dtos;

/// <summary>Student reserves a bed in a room. The student is taken from the token.</summary>
public record CreateBookingRequest
{
    [Required] public Guid RoomId { get; init; }
    /// <summary>Optional specific bed; if omitted, the first available bed is picked.</summary>
    public Guid? BedId { get; init; }
    [Required, MaxLength(20)] public string AcademicYear { get; init; } = default!;
}

/// <summary>Confirms the held payment (called after Paystack success / webhook).</summary>
public record ConfirmPaymentRequest
{
    [Required, MaxLength(100)] public string PaystackReference { get; init; } = default!;
}

/// <summary>Owner/worker confirms a student's arrival using the check-in code.</summary>
public record CheckInRequest
{
    [Required, MaxLength(20)] public string CheckInCode { get; init; } = default!;
}

/// <summary>Student raises a dispute inside the 48h window (room not as advertised, etc.).</summary>
public record RaiseDisputeRequest
{
    [Required, MaxLength(1000)] public string Reason { get; init; } = default!;
}

/// <summary>Support closes a dispute: refund the student, or release escrow to the owner.</summary>
public record ResolveDisputeRequest
{
    /// <summary>"refund" | "release".</summary>
    [Required] public DisputeOutcome Outcome { get; init; }

    [MaxLength(1000)] public string? Note { get; init; }
}

public record BookingResponse
{
    public Guid Id { get; init; }
    public Guid HostelId { get; init; }
    public string HostelName { get; init; } = default!;

    /// <summary>Cover photo of the hostel, site-relative (e.g. /uploads/...). Null if none.</summary>
    public string? HostelPhotoUrl { get; init; }
    public Guid RoomId { get; init; }
    public string RoomLabel { get; init; } = default!;
    public Guid BedId { get; init; }
    public string BedLabel { get; init; } = default!;
    public string AcademicYear { get; init; } = default!;
    public int Amount { get; init; }
    public int Commission { get; init; }
    public string Status { get; init; } = default!;
    public string? CheckInCode { get; init; }
    public string? PaystackReference { get; init; }
    public string? DisputeReason { get; init; }
    public string? DisputeResolution { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? PaidAt { get; init; }
    public DateTime? CheckedInAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public DateTime? DisputedAt { get; init; }
    public DateTime? ResolvedAt { get; init; }
}

/// <summary>
/// Someone else sharing the room. Deliberately thin: a name, a photo and what
/// they study. No phone or email — students have not consented to sharing
/// contact details with whoever happens to book the next bed.
/// </summary>
public record RoommateResponse
{
    public Guid UserId { get; init; }
    public string Name { get; init; } = default!;
    public string? PhotoUrl { get; init; }
    public string? Course { get; init; }
    public string? Level { get; init; }
    public string BedLabel { get; init; } = default!;

    /// <summary>True once they have actually moved in.</summary>
    public bool HasCheckedIn { get; init; }
}
