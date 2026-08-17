using MeDan.Api.Data;
using MeDan.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MeDan.Api.Services;

/// <summary>
/// Share codes and reward state for "Refer &amp; Earn". A user has one canonical code
/// (<see cref="AppUser.ReferralCode"/>); every friend who signs up with it gets a
/// <see cref="Referral"/> row, which turns Claimed once that friend completes their
/// first booking.
/// </summary>
public class ReferralService
{
    /// <summary>No 0/O/1/I — codes get read aloud and typed by hand.</summary>
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 6;

    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<ReferralService> _log;

    public ReferralService(AppDbContext db, IConfiguration config, ILogger<ReferralService> log)
    {
        _db = db;
        _config = config;
        _log = log;
    }

    /// <summary>Reward per successful referral, GH₵ (matches the app's <c>kReferralReward</c>).</summary>
    public int RewardAmount => _config.GetValue("Referrals:RewardAmount", 20);

    public string ShareUrlFor(string code) =>
        $"{_config["Referrals:ShareBaseUrl"]?.TrimEnd('/') ?? "https://medan.app/r"}/{code}";

    /// <summary>Wording mirrors the app's <c>Referral.shareMessage</c>.</summary>
    public string ShareMessageFor(string code) =>
        "I use MeDan to find verified hostels at UENR/USTED — no scams, MoMo payment held " +
        $"in escrow. Sign up with my link and we both get GH₵{RewardAmount}: {ShareUrlFor(code)}";

    /// <summary>
    /// Returns the user's canonical code, creating it on first use. Does not save —
    /// the caller owns the transaction.
    /// </summary>
    public async Task<string> EnsureCodeAsync(AppUser user, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(user.ReferralCode)) return user.ReferralCode;

        // Collisions are vanishingly rare at 32^6, but a unique index is enforcing this.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var code = Generate();
            if (!await _db.Users.AnyAsync(u => u.ReferralCode == code, ct))
            {
                user.ReferralCode = code;
                return code;
            }
        }

        throw new InvalidOperationException("Could not allocate a unique referral code.");
    }

    /// <summary>
    /// Called when a booking reaches Completed: if this student was referred and the reward
    /// is still Pending, mark it Claimed. Does not save — the caller owns the transaction.
    /// </summary>
    public async Task GrantRewardIfEligibleAsync(Guid refereeUserId, Guid bookingId, CancellationToken ct = default)
    {
        var referral = await _db.Referrals
            .FirstOrDefaultAsync(r => r.RefereeUserId == refereeUserId && r.Status == ReferralStatus.Pending, ct);
        if (referral is null) return;

        referral.Status = ReferralStatus.Claimed;
        referral.ClaimedAt = DateTime.UtcNow;
        referral.QualifyingBookingId = bookingId;

        _log.LogInformation(
            "Referral {Code} claimed: referrer {Referrer} earns GH₵{Amount} from booking {Booking}.",
            referral.Code, referral.ReferrerUserId, referral.RewardAmount, bookingId);
    }

    private static string Generate()
    {
        var chars = new char[CodeLength];
        for (var i = 0; i < CodeLength; i++)
            chars[i] = Alphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }
}
