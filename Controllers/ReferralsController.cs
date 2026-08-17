using MeDan.Api.Auth;
using MeDan.Api.Data;
using MeDan.Api.Dtos;
using MeDan.Api.Helpers;
using MeDan.Api.Models;
using MeDan.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MeDan.Api.Controllers;

/// <summary>
/// Refer &amp; Earn. Each user has one canonical share code; a friend attaches it once at
/// signup, and the reward flips Pending → Claimed when that friend completes their first
/// booking (see <see cref="BookingsController.Complete"/>) → Paid once support pays out.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReferralsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly CurrentUser _current;
    private readonly ReferralService _referrals;

    public ReferralsController(AppDbContext db, CurrentUser current, ReferralService referrals)
    {
        _db = db;
        _current = current;
        _referrals = referrals;
    }

    /// <summary>The caller's share code (created on first call) + earnings summary.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<MyReferralResponse>> Me(CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return Unauthorized("Register first.");

        var code = await _referrals.EnsureCodeAsync(me, ct);
        await _db.SaveChangesAsync(ct);

        var mine = await _db.Referrals.Where(r => r.ReferrerUserId == me.Id).ToListAsync(ct);

        return new MyReferralResponse
        {
            Code = code,
            ShareUrl = _referrals.ShareUrlFor(code),
            ShareMessage = _referrals.ShareMessageFor(code),
            RewardAmount = _referrals.RewardAmount,
            TotalReferrals = mine.Count,
            PendingCount = mine.Count(r => r.Status == ReferralStatus.Pending),
            ClaimedCount = mine.Count(r => r.Status == ReferralStatus.Claimed),
            PaidCount = mine.Count(r => r.Status == ReferralStatus.Paid),
            TotalEarned = mine.Where(r => r.Status != ReferralStatus.Pending).Sum(r => r.RewardAmount),
            PendingPayout = mine.Where(r => r.Status == ReferralStatus.Claimed).Sum(r => r.RewardAmount)
        };
    }

    /// <summary>Everyone who signed up with the caller's code, newest first.</summary>
    [HttpGet("mine")]
    public async Task<ActionResult<IEnumerable<ReferralResponse>>> Mine(CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return Unauthorized("Register first.");

        var list = await _db.Referrals
            .Where(r => r.ReferrerUserId == me.Id)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        return list.Select(ToResponse).ToList();
    }

    /// <summary>
    /// Attach a code to the caller — call once, right after registration. The referee is the
    /// authenticated user (never trusted from the body).
    /// </summary>
    [HttpPost("attach")]
    public async Task<ActionResult<ReferralResponse>> Attach(AttachReferralRequest req, CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return Unauthorized("Register first.");

        var code = req.Code.Trim().ToUpperInvariant();

        if (string.Equals(me.ReferralCode, code, StringComparison.OrdinalIgnoreCase))
            return BadRequest("You cannot use your own referral code.");

        if (await _db.Referrals.AnyAsync(r => r.RefereeUserId == me.Id, ct))
            return Conflict("You have already used a referral code.");

        var referrer = await _db.Users.FirstOrDefaultAsync(u => u.ReferralCode == code, ct);
        if (referrer is null) return NotFound("That referral code doesn't exist.");
        if (referrer.Id == me.Id) return BadRequest("You cannot use your own referral code.");

        var referral = new Referral
        {
            Code = code,
            ReferrerUserId = referrer.Id,
            ReferrerName = referrer.FullName,
            RefereeUserId = me.Id,
            RefereeName = me.FullName,
            Status = ReferralStatus.Pending,
            RewardAmount = _referrals.RewardAmount
        };

        _db.Referrals.Add(referral);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Mine), null, ToResponse(referral));
    }

    /// <summary>Who referred the caller, if anyone.</summary>
    [HttpGet("referrer")]
    public async Task<ActionResult<ReferralResponse>> Referrer(CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return Unauthorized("Register first.");

        var referral = await _db.Referrals.FirstOrDefaultAsync(r => r.RefereeUserId == me.Id, ct);
        return referral is null ? NotFound("You weren't referred by anyone.") : ToResponse(referral);
    }

    /// <summary>Mark a claimed reward as paid out. Platform staff only.</summary>
    [HttpPost("{id:guid}/mark-paid")]
    public async Task<ActionResult<ReferralResponse>> MarkPaid(Guid id, CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return Unauthorized("Register first.");
        if (me.Role is not (UserRole.Admin or UserRole.Manager)) return Forbid();

        var referral = await _db.Referrals.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (referral is null) return NotFound();
        if (referral.Status != ReferralStatus.Claimed)
            return Conflict($"Cannot pay a referral in state {referral.Status}.");

        referral.Status = ReferralStatus.Paid;
        referral.PaidAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return ToResponse(referral);
    }

    private static ReferralResponse ToResponse(Referral r) => new()
    {
        Id = r.Id,
        Code = r.Code,
        ReferrerUserId = r.ReferrerUserId,
        ReferrerName = r.ReferrerName,
        RefereeUserId = r.RefereeUserId,
        RefereeName = r.RefereeName,
        Status = r.Status.ToCamel(),
        RewardAmount = r.RewardAmount,
        QualifyingBookingId = r.QualifyingBookingId,
        ClaimedAt = r.ClaimedAt,
        PaidAt = r.PaidAt,
        CreatedAt = r.CreatedAt
    };
}
