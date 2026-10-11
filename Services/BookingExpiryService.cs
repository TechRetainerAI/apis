using MeDan.Api.Data;
using MeDan.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MeDan.Api.Services;

/// <summary>
/// Cancels unpaid bookings whose hold has run out and puts their beds back.
///
/// A <see cref="BookingStatus.Pending"/> booking reserves a real bed before any
/// money moves. Nothing used to take it back, so a student who opened the booking
/// screen and never paid held that bed indefinitely — the room advertised one
/// fewer bed than it had free, permanently, and a student looking at the listing
/// saw a full room that nobody was actually in.
///
/// Paid bookings are never touched: <see cref="Booking.ExpiresAt"/> is cleared the
/// moment payment is held.
/// </summary>
public class BookingExpiryService : BackgroundService
{
    /// <summary>
    /// How long a student has to pay before the bed goes back. Short enough that an
    /// abandoned booking does not block the room for the next student, long enough
    /// to finish a MoMo prompt and upload a screenshot.
    /// </summary>
    public static TimeSpan Hold { get; private set; } = TimeSpan.FromMinutes(30);

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<BookingExpiryService> _log;

    public BookingExpiryService(
        IServiceScopeFactory scopes, IConfiguration config, ILogger<BookingExpiryService> log)
    {
        _scopes = scopes;
        _log = log;

        var minutes = config.GetValue<int?>("Booking:HoldMinutes");
        if (minutes is > 0) Hold = TimeSpan.FromMinutes(minutes.Value);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _log.LogInformation(
            "Booking expiry sweeper started (every {Interval}, hold {Hold}).", Interval, Hold);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Never let one bad pass kill the loop — the next tick retries.
                _log.LogError(ex, "Booking expiry sweep failed; will retry next interval.");
            }

            try { await Task.Delay(Interval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;

        var expired = await db.Bookings
            .Where(b => b.Status == BookingStatus.Pending
                        && b.ExpiresAt != null
                        && b.ExpiresAt <= now)
            .OrderBy(b => b.ExpiresAt)
            .Take(50)                      // bounded so one pass can't run away
            .ToListAsync(ct);

        if (expired.Count == 0) return;

        _log.LogInformation("{Count} booking(s) past their payment hold.", expired.Count);

        foreach (var booking in expired)
        {
            if (ct.IsCancellationRequested) break;

            booking.Status = BookingStatus.Cancelled;
            booking.ExpiresAt = null;
            await RoomAvailability.ReleaseBedAsync(db, booking, ct);
            await db.SaveChangesAsync(ct);

            _log.LogInformation(
                "Booking {Booking} expired unpaid; bed {Bed} is back on the market.",
                booking.Id, booking.BedId);
        }
    }
}
