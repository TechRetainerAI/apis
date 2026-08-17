using MeDan.Api.Models;

namespace MeDan.Api.Services;

/// <summary>
/// The wording for every notification MeDan sends, in one place.
///
/// Keeping the copy here rather than scattered through controllers means the
/// student-facing voice stays consistent, and the `type`/`route` data keys —
/// which the app uses to pick an icon and to deep-link on tap — cannot drift
/// out of step with each other.
/// </summary>
public class BookingNotifier
{
    private readonly PushSender _push;
    private readonly ILogger<BookingNotifier> _log;

    public BookingNotifier(PushSender push, ILogger<BookingNotifier> log)
    {
        _push = push;
        _log = log;
    }

    /// <summary>Payment settled — the money is now held in escrow.</summary>
    public Task PaymentHeldAsync(Booking booking, CancellationToken ct = default) =>
        SendAsync(booking.StudentUserId, new PushMessage(
            "Payment received",
            $"Your GH₵{booking.Amount} is held safely until you check in. " +
            "Show your check-in code when you arrive.",
            Data(booking, "payment")), ct);

    /// <summary>Owner accepted the check-in code; the dispute window opens.</summary>
    public Task CheckedInAsync(Booking booking, CancellationToken ct = default) =>
        SendAsync(booking.StudentUserId, new PushMessage(
            "Check-in confirmed",
            "You're checked in. If anything is wrong with the room, report it " +
            "within 48 hours — after that the payment is released to the owner.",
            Data(booking, "checkIn")), ct);

    /// <summary>Escrow released to the owner after the window closed.</summary>
    public Task ReleasedAsync(Booking booking, CancellationToken ct = default) =>
        SendAsync(booking.StudentUserId, new PushMessage(
            "Booking complete",
            "The dispute window has closed and your payment has been released " +
            "to the hostel. Enjoy your stay.",
            Data(booking, "payout")), ct);

    /// <summary>Money returned to the student.</summary>
    public Task RefundedAsync(Booking booking, CancellationToken ct = default) =>
        SendAsync(booking.StudentUserId, new PushMessage(
            "Refund on the way",
            $"GH₵{booking.Amount} is being returned to you. It can take a few " +
            "days to reach your wallet.",
            Data(booking, "refund")), ct);

    /// <summary>A student raised a dispute — tell the hostel's owner.</summary>
    public Task DisputeRaisedAsync(Booking booking, Guid ownerUserId, CancellationToken ct = default) =>
        SendAsync(ownerUserId, new PushMessage(
            "A booking was disputed",
            "A student has reported a problem. The payment is frozen until " +
            "MeDan support resolves it.",
            Data(booking, "booking")), ct);

    /// <summary>A new booking landed — tell the hostel's owner.</summary>
    public Task NewBookingAsync(Booking booking, Guid ownerUserId, CancellationToken ct = default) =>
        SendAsync(ownerUserId, new PushMessage(
            "New booking",
            $"A student booked a bed for GH₵{booking.Amount}. You'll be paid " +
            "48 hours after they check in.",
            Data(booking, "booking")), ct);

    private static Dictionary<string, string> Data(Booking booking, string type) => new()
    {
        ["type"] = type,
        ["bookingId"] = booking.Id.ToString(),
        // Consumed by pushDeepLinkProvider in the app.
        ["route"] = $"/bookings/{booking.Id}"
    };

    /// <summary>
    /// Never lets a notification failure escape. Callers are inside booking and
    /// payment flows — a dropped push must not roll one of those back.
    /// </summary>
    private async Task SendAsync(Guid userId, PushMessage message, CancellationToken ct)
    {
        try
        {
            await _push.SendToUserAsync(userId, message, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Notification \"{Title}\" not delivered.", message.Title);
        }
    }
}
