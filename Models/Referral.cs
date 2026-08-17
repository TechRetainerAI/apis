using System.ComponentModel.DataAnnotations;

namespace MeDan.Api.Models;

/// <summary>
/// One referral relationship: a friend (<see cref="RefereeUserId"/>) signed up with a
/// referrer's share code. The referrer keeps a single canonical code
/// (<see cref="AppUser.ReferralCode"/>) — this row records each person who used it and
/// the reward state for that signup.
/// </summary>
public class Referral
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The share code that was used — the referrer's canonical code, e.g. "ABC123".</summary>
    [MaxLength(20)]
    public string Code { get; set; } = default!;

    public Guid ReferrerUserId { get; set; }
    public AppUser Referrer { get; set; } = default!;

    /// <summary>Denormalized so the earnings list doesn't need a join.</summary>
    [MaxLength(150)]
    public string ReferrerName { get; set; } = default!;

    /// <summary>The friend who used the code. Set the moment the code is attached.</summary>
    public Guid? RefereeUserId { get; set; }
    public AppUser? Referee { get; set; }

    [MaxLength(150)]
    public string? RefereeName { get; set; }

    /// <summary>Pending → Claimed (referee completed their first booking) → Paid (payout done).</summary>
    public ReferralStatus Status { get; set; } = ReferralStatus.Pending;

    /// <summary>Reward per successful signup, GH₵.</summary>
    public int RewardAmount { get; set; } = 20;

    /// <summary>The referee's first completed booking — what unlocked the reward.</summary>
    public Guid? QualifyingBookingId { get; set; }

    public DateTime? ClaimedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
