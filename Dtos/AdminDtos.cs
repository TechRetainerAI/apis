using System.ComponentModel.DataAnnotations;
using MeDan.Api.Models;

namespace MeDan.Api.Dtos;

/// <summary>Counts for the admin dashboard landing page.</summary>
public record AdminStatsResponse
{
    public int Users { get; init; }
    public int Students { get; init; }
    public int Staff { get; init; }          // Owner + Worker + Manager + Admin
    public int Companies { get; init; }
    public int Hostels { get; init; }
    public int UnverifiedHostels { get; init; }
    public int Bookings { get; init; }
    public int OpenDisputes { get; init; }
    public int ReferralsAwaitingPayout { get; init; }

    /// <summary>Escrow currently held (PaymentHeld + CheckedIn + Disputed), GH₵.</summary>
    public int EscrowHeld { get; init; }

    /// <summary>Paid bookings whose students have not been checked in yet.</summary>
    public int AwaitingCheckIn { get; init; }

    /// <summary>
    /// Payouts sitting Pending — released escrow that has not reached the
    /// owner, usually because the company has no settlement account.
    /// </summary>
    public int StuckPayouts { get; init; }

    /// <summary>Events scheduled from now on.</summary>
    public int UpcomingEvents { get; init; }
}

/// <summary>A booking as support sees it — adds the student/company the student-facing DTO omits.</summary>
public record AdminBookingResponse
{
    public Guid Id { get; init; }
    public Guid StudentUserId { get; init; }
    public string StudentName { get; init; } = default!;
    public string StudentEmail { get; init; } = default!;
    public string? StudentPhone { get; init; }
    public Guid HostelId { get; init; }
    public string HostelName { get; init; } = default!;
    public string RoomLabel { get; init; } = default!;
    public string BedLabel { get; init; } = default!;
    public Guid CompanyId { get; init; }
    public string CompanyName { get; init; } = default!;
    public string AcademicYear { get; init; } = default!;
    public int Amount { get; init; }
    public int Commission { get; init; }
    public string Status { get; init; } = default!;
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

public record AdminUserResponse
{
    public Guid Id { get; init; }
    public string Email { get; init; } = default!;
    public string Name { get; init; } = default!;
    public string? Phone { get; init; }
    public string? PhotoUrl { get; init; }
    public string Role { get; init; } = default!;
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public int BookingCount { get; init; }
}

/// <summary>Change a user's platform role (e.g. promote an employee to Worker/Manager).</summary>
public record SetRoleRequest
{
    [Required] public UserRole Role { get; init; }
}

/// <summary>Body for POST /api/admin/users — onboarding a hostel manager.</summary>
public record AdminCreateUserRequest
{
    [Required, MaxLength(150)] public string Name { get; init; } = default!;
    [Required, EmailAddress, MaxLength(256)] public string Email { get; init; } = default!;
    [MaxLength(30)] public string? Phone { get; init; }

    /// <summary>Temporary password, shown to the admin once. Min 8 characters.</summary>
    [Required, MaxLength(100)] public string Password { get; init; } = default!;

    /// <summary>camelCase role: owner | worker | manager. Never admin.</summary>
    [Required] public string Role { get; init; } = "owner";
}

/// <summary>Flip a listing's verified badge.</summary>
public record SetVerifiedRequest
{
    public bool Verified { get; init; } = true;
}
