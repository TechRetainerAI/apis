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

        return (true, null);
    }
}
