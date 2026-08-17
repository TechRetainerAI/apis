using System.ComponentModel.DataAnnotations;

namespace MeDan.Api.Dtos;

/// <summary>A friend signs up with someone's code. The referee is taken from the token.</summary>
public record AttachReferralRequest
{
    [Required, MaxLength(20)] public string Code { get; init; } = default!;
}

/// <summary>The caller's own share code plus their earnings summary.</summary>
public record MyReferralResponse
{
    public string Code { get; init; } = default!;
    public string ShareUrl { get; init; } = default!;
    public string ShareMessage { get; init; } = default!;

    /// <summary>Reward per successful referral, GH₵.</summary>
    public int RewardAmount { get; init; }

    public int TotalReferrals { get; init; }
    public int PendingCount { get; init; }
    public int ClaimedCount { get; init; }
    public int PaidCount { get; init; }

    /// <summary>Sum of claimed + paid rewards, GH₵.</summary>
    public int TotalEarned { get; init; }

    /// <summary>Claimed but not yet paid out, GH₵.</summary>
    public int PendingPayout { get; init; }
}

public record ReferralResponse
{
    public Guid Id { get; init; }
    public string Code { get; init; } = default!;
    public Guid ReferrerUserId { get; init; }
    public string ReferrerName { get; init; } = default!;
    public Guid? RefereeUserId { get; init; }
    public string? RefereeName { get; init; }
    public string Status { get; init; } = default!;
    public int RewardAmount { get; init; }
    public Guid? QualifyingBookingId { get; init; }
    public DateTime? ClaimedAt { get; init; }
    public DateTime? PaidAt { get; init; }
    public DateTime CreatedAt { get; init; }
}
