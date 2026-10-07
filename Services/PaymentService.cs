using MeDan.Api.Data;
using MeDan.Api.Models;

namespace MeDan.Api.Services;

/// <summary>
/// The single place a verified Paystack result is turned into escrow state, so
/// <c>PaymentsController</c> (verify + webhook) and <c>BookingsController.ConfirmPayment</c>
/// can't drift apart.
/// </summary>
public class PaymentService
{
    private readonly AppDbContext _db;
    private readonly BookingNotifier _notify;
    private readonly ILogger<PaymentService> _log;

    public PaymentService(AppDbContext db, BookingNotifier notify, ILogger<PaymentService> log)
    {
        _db = db;
        _notify = notify;
        _log = log;
    }

    /// <summary>
    /// Applies a verified result to the payment + its booking and saves. Idempotent: a booking
    /// already past Pending is left as-is. Returns an error message when the result can't be
    /// trusted (e.g. underpayment), in which case nothing is advanced.
    /// </summary>
    public async Task<(bool Ok, string? Error)> ApplyAsync(
        Payment payment, Booking booking, PaystackVerifyResult result, CancellationToken ct = default)
    {
        if (result.Status != PaymentStatus.Success)
        {
            payment.Status = result.Status;
            await _db.SaveChangesAsync(ct);
            return (true, null);
        }

        // Guard against a reference that settled for less than the booking is worth.
        // (Simulation reports 0 — nothing to compare against.)
        var expected = booking.Amount * 100;
        if (result.AmountPesewas > 0 && result.AmountPesewas < expected)
        {
            _log.LogError(
                "Underpayment on {Reference}: got {Got} pesewas, expected {Expected}.",
                payment.Reference, result.AmountPesewas, expected);
            return (false, "The amount paid does not match the booking.");
        }

        payment.Status = PaymentStatus.Success;
        payment.Channel = result.Channel;

        await HoldAsync(payment, booking, ct);
        return (true, null);
    }

    /// <summary>
    /// Accepts a manual Mobile Money transfer after staff checked the screenshot, and
    /// moves the booking into escrow by exactly the route a Paystack success takes.
    ///
    /// Staff are standing in for the provider here, so this is the one place money can
    /// enter escrow on somebody's say-so — hence the reviewer is recorded on the row.
    /// </summary>
    public async Task<(bool Ok, string? Error)> ApproveManualAsync(
        Payment payment, Booking booking, Guid reviewerId, CancellationToken ct = default)
    {
        if (payment.Channel != PaymentChannel.ManualMomo)
            return (false, "This payment is not a manual transfer.");

        if (payment.Status == PaymentStatus.Success)
            return (true, null);   // already approved; don't pay twice

        if (payment.Status != PaymentStatus.PendingReview)
            return (false, $"Cannot approve a payment in state {payment.Status}.");

        payment.Status = PaymentStatus.Success;
        payment.ReviewedByUserId = reviewerId;
        payment.ReviewedAt = DateTime.UtcNow;
        payment.ReviewNote = null;

        _log.LogInformation(
            "Manual payment {Reference} approved by {Reviewer} for booking {Booking}.",
            payment.Reference, reviewerId, booking.Id);

        await HoldAsync(payment, booking, ct);
        return (true, null);
    }

    /// <summary>
    /// Turns a manual transfer down. The booking stays Pending and keeps its bed, so the
    /// student can fix the problem named in <paramref name="reason"/> and submit again.
    /// </summary>
    public async Task<(bool Ok, string? Error)> RejectManualAsync(
        Payment payment, Booking booking, Guid reviewerId, string reason, CancellationToken ct = default)
    {
        if (payment.Channel != PaymentChannel.ManualMomo)
            return (false, "This payment is not a manual transfer.");

        if (payment.Status == PaymentStatus.Success)
            return (false, "This payment was already approved; refund it instead.");

        payment.Status = PaymentStatus.Rejected;
        payment.ReviewedByUserId = reviewerId;
        payment.ReviewedAt = DateTime.UtcNow;
        payment.ReviewNote = reason;

        await _db.SaveChangesAsync(ct);

        _log.LogInformation(
            "Manual payment {Reference} rejected by {Reviewer}: {Reason}",
            payment.Reference, reviewerId, reason);

        await _notify.ManualPaymentRejectedAsync(booking, reason, ct);
        return (true, null);
    }

    /// <summary>
    /// The one transition into escrow, shared by the Paystack and manual routes.
    /// Idempotent: a booking already past Pending is saved but not re-announced.
    /// </summary>
    private async Task HoldAsync(Payment payment, Booking booking, CancellationToken ct)
    {
        // Only true on the transition, so re-verifying a settled payment does
        // not notify the student twice.
        var justHeld = false;

        if (booking.Status == BookingStatus.Pending)
        {
            booking.Status = BookingStatus.PaymentHeld;
            booking.PaidAt = DateTime.UtcNow;
            booking.PaystackReference = payment.Reference;
            justHeld = true;
            _log.LogInformation(
                "Payment {Reference} held in escrow for booking {Booking} (GH₵{Amount}).",
                payment.Reference, booking.Id, booking.Amount);
        }

        await _db.SaveChangesAsync(ct);

        // After the save — a notification about a state that failed to persist
        // would be worse than no notification at all.
        if (justHeld) await _notify.PaymentHeldAsync(booking, ct);
    }
}
