using MeDan.Api.Data;
using MeDan.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MeDan.Api.Services;

/// <summary>
/// Closes out escrow without anyone having to ask.
///
/// Two jobs, run on a loop:
/// 1. Any booking checked in longer than the dispute window and not disputed is
///    completed and released to the owner. Before this existed, release was
///    owner-triggered — so an owner who never pressed the button left the
///    student's money sitting in escrow indefinitely.
/// 2. Payouts left <see cref="PayoutStatus.Pending"/> by a transport failure or a
///    missing payout account are retried. Retrying is safe: the reference is the
///    idempotency key, so Paystack rejects a genuine duplicate.
/// </summary>
public class EscrowReleaseService : BackgroundService
{
    /// <summary>Matches the window enforced by BookingsController.</summary>
    public static readonly TimeSpan DisputeWindow = TimeSpan.FromHours(48);

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    /// <summary>Give a failed payout room to breathe before trying again.</summary>
    private static readonly TimeSpan RetryBackoff = TimeSpan.FromHours(1);

    private const int MaxAttempts = 8;

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<EscrowReleaseService> _log;

    public EscrowReleaseService(IServiceScopeFactory scopes, ILogger<EscrowReleaseService> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _log.LogInformation(
            "Escrow release sweeper started (every {Interval}, window {Window}).",
            Interval, DisputeWindow);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Never let one bad pass kill the loop — the next tick retries.
                _log.LogError(ex, "Escrow sweep failed; will retry next interval.");
            }

            try { await Task.Delay(Interval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var payouts = scope.ServiceProvider.GetRequiredService<PayoutService>();
        var referrals = scope.ServiceProvider.GetRequiredService<ReferralService>();
        var notify = scope.ServiceProvider.GetRequiredService<BookingNotifier>();

        await ReleaseMaturedAsync(db, payouts, referrals, notify, ct);
        await RetryStuckPayoutsAsync(db, payouts, ct);
    }

    /// <summary>Completes and pays out every booking whose dispute window has closed.</summary>
    private async Task ReleaseMaturedAsync(
        AppDbContext db, PayoutService payouts, ReferralService referrals,
        BookingNotifier notify, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - DisputeWindow;

        var matured = await db.Bookings
            .Where(b => b.Status == BookingStatus.CheckedIn
                        && b.CheckedInAt != null
                        && b.CheckedInAt <= cutoff)
            .OrderBy(b => b.CheckedInAt)
            .Take(50)                       // bounded so one pass can't run away
            .ToListAsync(ct);

        if (matured.Count == 0) return;

        _log.LogInformation("{Count} booking(s) past the dispute window.", matured.Count);

        foreach (var booking in matured)
        {
            if (ct.IsCancellationRequested) break;

            booking.Status = BookingStatus.Completed;
            booking.CompletedAt = DateTime.UtcNow;
            await referrals.GrantRewardIfEligibleAsync(booking.StudentUserId, booking.Id, ct);
            await db.SaveChangesAsync(ct);

            var payout = await payouts.ReleaseAsync(booking, ct);
            await notify.ReleasedAsync(booking, ct);
            _log.LogInformation(
                "Auto-released booking {Booking}: payout {Status}.", booking.Id, payout.Status);
        }
    }

    /// <summary>Re-sends payouts that never got through.</summary>
    private async Task RetryStuckPayoutsAsync(
        AppDbContext db, PayoutService payouts, CancellationToken ct)
    {
        var retryBefore = DateTime.UtcNow - RetryBackoff;

        var stuck = await db.Payouts
            .Include(p => p.Booking)
            .Where(p => p.Status == PayoutStatus.Pending
                        && p.Attempts < MaxAttempts
                        && (p.LastAttemptAt == null || p.LastAttemptAt <= retryBefore))
            .OrderBy(p => p.CreatedAt)
            .Take(25)
            .ToListAsync(ct);

        if (stuck.Count == 0) return;

        _log.LogInformation("Retrying {Count} stuck payout(s).", stuck.Count);

        foreach (var payout in stuck)
        {
            if (ct.IsCancellationRequested) break;
            if (payout.Booking is null) continue;

            var result = payout.Kind == PayoutKind.Refund
                ? await payouts.RefundAsync(payout.Booking, ct)
                : await payouts.ReleaseAsync(payout.Booking, ct);

            if (result.Attempts >= MaxAttempts && result.Status == PayoutStatus.Pending)
            {
                _log.LogError(
                    "Payout {Reference} still pending after {Attempts} attempts — needs a human. Last error: {Error}",
                    result.Reference, result.Attempts, result.FailureReason);
            }
        }
    }
}
