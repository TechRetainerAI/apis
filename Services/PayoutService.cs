using MeDan.Api.Data;
using MeDan.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MeDan.Api.Services;

/// <summary>
/// Moves money out of escrow — to the hostel owner when a stay completes, or back
/// to the student when support refunds a dispute.
///
/// Every path is idempotent. A payout row is written and committed *before* Paystack
/// is called, and (BookingId, Kind) is unique in the database. If two requests race,
/// the second fails the insert and reuses the first row instead of paying twice. A
/// retry after a crash finds the existing row and resumes rather than re-sending.
/// </summary>
public class PayoutService
{
    private readonly AppDbContext _db;
    private readonly IPaystackClient _paystack;
    private readonly ILogger<PayoutService> _log;

    public PayoutService(AppDbContext db, IPaystackClient paystack, ILogger<PayoutService> log)
    {
        _db = db;
        _paystack = paystack;
        _log = log;
    }

    /// <summary>
    /// Releases a completed booking's escrow to the owner, less MeDan's commission.
    /// Returns the payout — check <see cref="Payout.Status"/> for the outcome.
    /// </summary>
    public async Task<Payout> ReleaseAsync(Booking booking, CancellationToken ct = default)
    {
        var net = Math.Max(0, booking.Amount - booking.Commission);

        var payout = await GetOrCreateAsync(
            booking, PayoutKind.Release, net, booking.CompanyId, ct);

        // Already settled (or settling) — never send again.
        if (payout.Status is PayoutStatus.Paid or PayoutStatus.Processing)
        {
            _log.LogInformation(
                "Release for booking {Booking} already {Status}; skipping.",
                booking.Id, payout.Status);
            return payout;
        }

        var company = await _db.Companies
            .FirstOrDefaultAsync(c => c.Id == booking.CompanyId, ct);

        if (company?.PaystackRecipientCode is not { Length: > 0 } recipient)
        {
            // Not a failure of the transfer — there is simply nowhere to send it yet.
            // Left Pending so it can be retried once the owner adds an account.
            payout.FailureReason =
                "The hostel's company has no payout account set up yet.";
            payout.LastAttemptAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            _log.LogWarning(
                "Cannot release booking {Booking}: company {Company} has no recipient.",
                booking.Id, booking.CompanyId);
            return payout;
        }

        payout.Attempts++;
        payout.LastAttemptAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        try
        {
            var result = await _paystack.TransferAsync(
                recipient,
                payout.Amount,
                payout.Reference,
                $"MeDan booking {booking.Id}",
                ct);

            payout.ProviderReference = result.TransferCode;
            payout.Status = result.Status;
            payout.FailureReason = result.Status == PayoutStatus.Failed ? result.Message : null;
            if (result.Status == PayoutStatus.Paid) payout.SettledAt = DateTime.UtcNow;

            _log.LogInformation(
                "Release {Reference} for booking {Booking}: {Status} (GH¢{Amount}).",
                payout.Reference, booking.Id, payout.Status, payout.Amount);
        }
        catch (PaystackException ex)
        {
            // Leave it Pending, not Failed — the money may not have moved, and a
            // sweep can retry safely because the reference is the idempotency key.
            payout.FailureReason = ex.Message;
            _log.LogError(ex, "Transfer failed for booking {Booking}.", booking.Id);
        }

        await _db.SaveChangesAsync(ct);
        return payout;
    }

    /// <summary>Returns a disputed booking's money to the student.</summary>
    public async Task<Payout> RefundAsync(Booking booking, CancellationToken ct = default)
    {
        var payout = await GetOrCreateAsync(
            booking, PayoutKind.Refund, booking.Amount, null, ct);

        if (payout.Status is PayoutStatus.Refunded or PayoutStatus.Processing)
            return payout;

        if (booking.PaystackReference is not { Length: > 0 } charge)
        {
            payout.FailureReason =
                "This booking has no settled payment to refund.";
            await _db.SaveChangesAsync(ct);
            return payout;
        }

        payout.Attempts++;
        payout.LastAttemptAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        try
        {
            var result = await _paystack.RefundAsync(charge, payout.Amount, ct);

            payout.ProviderReference = result.RefundId;
            payout.Status = result.Status;
            payout.FailureReason = result.Status == PayoutStatus.Failed ? result.Message : null;
            if (result.Status == PayoutStatus.Refunded) payout.SettledAt = DateTime.UtcNow;

            _log.LogInformation(
                "Refund {Reference} for booking {Booking}: {Status}.",
                payout.Reference, booking.Id, payout.Status);
        }
        catch (PaystackException ex)
        {
            payout.FailureReason = ex.Message;
            _log.LogError(ex, "Refund failed for booking {Booking}.", booking.Id);
        }

        await _db.SaveChangesAsync(ct);
        return payout;
    }

    /// <summary>
    /// Finds this booking's payout of the given kind, or creates it. The unique
    /// index on (BookingId, Kind) is what makes concurrent callers converge on one
    /// row instead of both sending money.
    /// </summary>
    private async Task<Payout> GetOrCreateAsync(
        Booking booking, PayoutKind kind, int amount, Guid? companyId, CancellationToken ct)
    {
        var existing = await _db.Payouts
            .FirstOrDefaultAsync(p => p.BookingId == booking.Id && p.Kind == kind, ct);
        if (existing is not null) return existing;

        var payout = new Payout
        {
            BookingId = booking.Id,
            CompanyId = companyId,
            Kind = kind,
            Amount = amount,
            Reference = BuildReference(booking.Id, kind),
        };

        _db.Payouts.Add(payout);
        try
        {
            await _db.SaveChangesAsync(ct);
            return payout;
        }
        catch (DbUpdateException)
        {
            // Lost the race — another request created it. Use theirs.
            _db.Entry(payout).State = EntityState.Detached;
            return await _db.Payouts
                .FirstAsync(p => p.BookingId == booking.Id && p.Kind == kind, ct);
        }
    }

    /// <summary>
    /// Deterministic per (booking, kind), so a replay produces the same reference
    /// and Paystack rejects the duplicate rather than sending money again.
    /// </summary>
    private static string BuildReference(Guid bookingId, PayoutKind kind) =>
        $"medan-{(kind == PayoutKind.Refund ? "rfd" : "rel")}-{bookingId:N}";
}
