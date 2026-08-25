using System.Text.Json;
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
/// Paystack payments for a booking. The secret key stays server-side: the app gets a
/// reference (+ checkout URL) from <c>initialize</c>, sends the customer through Paystack,
/// then either polls <c>verify</c> or waits for the <c>webhook</c> — both funnel into the
/// same transition that moves the booking to <see cref="BookingStatus.PaymentHeld"/>.
///
/// Escrow *release* is not here: that's <c>POST /api/bookings/{id}/complete</c>, which owns
/// the booking state machine.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly CurrentUser _current;
    private readonly IPaystackClient _paystack;
    private readonly PaymentService _payments;
    private readonly ILogger<PaymentsController> _log;

    public PaymentsController(
        AppDbContext db,
        CurrentUser current,
        IPaystackClient paystack,
        PaymentService payments,
        ILogger<PaymentsController> log)
    {
        _db = db;
        _current = current;
        _paystack = paystack;
        _payments = payments;
        _log = log;
    }

    /// <summary>Start a transaction for a pending booking. Returns the reference + checkout URL.</summary>
    [HttpPost("initialize")]
    public async Task<ActionResult<PaymentResponse>> Initialize(InitializePaymentRequest req, CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return Unauthorized("Register first.");

        var booking = await _db.Bookings
            .Include(b => b.Payment)
            .FirstOrDefaultAsync(b => b.Id == req.BookingId, ct);

        if (booking is null) return NotFound("Booking not found.");
        if (booking.StudentUserId != me.Id) return Forbid();
        if (booking.Status != BookingStatus.Pending)
            return Conflict($"Cannot pay a booking in state {booking.Status}.");

        // One Payment row per booking: a successful one is final, anything else is a
        // stale attempt the student is retrying.
        if (booking.Payment is { Status: PaymentStatus.Success })
            return Conflict("This booking has already been paid.");
        if (booking.Payment is not null) _db.Payments.Remove(booking.Payment);

        var reference = PaystackClient.NewReference();
        var metadata = new Dictionary<string, string>
        {
            ["bookingId"] = booking.Id.ToString(),
            ["hostelId"] = booking.HostelId.ToString(),
            ["studentUserId"] = me.Id.ToString()
        };

        var phone = req.Phone ?? me.Phone;
        var isMomo = req.Channel is PaymentChannel.MomoMtn or PaymentChannel.MomoTelecel;

        var requiresOtp = false;
        string? displayText = null;

        PaystackInitResult init;
        try
        {
            // Mobile Money goes through the Charge API so the approval prompt
            // lands on the student's handset. Routing MoMo through the hosted
            // checkout instead left transactions sitting "abandoned" whenever
            // the page was not completed — which was most of the time.
            if (isMomo && !string.IsNullOrWhiteSpace(phone))
            {
                var charge = await _paystack.ChargeMobileMoneyAsync(
                    email: me.Email,
                    amountGhs: booking.Amount,
                    reference: reference,
                    channel: req.Channel,
                    phone: phone,
                    metadata: metadata,
                    ct: ct);

                if (charge.State == PaystackChargeState.Failed)
                {
                    _log.LogWarning(
                        "MoMo charge rejected for booking {Booking}: {Message}",
                        booking.Id, charge.Message);
                    return StatusCode(
                        StatusCodes.Status502BadGateway,
                        charge.Message ?? "The mobile money charge was declined.");
                }

                // No checkout URL for this path — the phone is the interface.
                init = new PaystackInitResult(charge.Reference, null, null);
                requiresOtp = charge.State == PaystackChargeState.NeedsOtp;
                displayText = charge.DisplayText ?? charge.Message;
            }
            else
            {
                init = await _paystack.InitializeAsync(
                    email: me.Email,
                    amountGhs: booking.Amount,
                    reference: reference,
                    channel: req.Channel,
                    phone: phone,
                    metadata: metadata,
                    ct: ct);
            }
        }
        catch (PaystackException ex)
        {
            _log.LogError(ex, "Paystack initialize failed for booking {Booking}.", booking.Id);
            return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            // No Paystack secret key configured — a deployment gap, not a
            // student problem. Say so plainly instead of a bare 500.
            _log.LogError(ex, "Payments unavailable: Paystack is not configured.");
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                "Payments are not switched on yet — please try again later.");
        }

        var payment = new Payment
        {
            Reference = init.Reference,
            BookingId = booking.Id,
            Amount = booking.Amount,
            Channel = req.Channel,
            Status = PaymentStatus.Initialized,
            CheckoutUrl = init.CheckoutUrl,
            AuthorizationCode = init.AccessCode
        };

        _db.Payments.Add(payment);
        booking.PaystackReference = payment.Reference;
        await _db.SaveChangesAsync(ct);

        return ToResponse(payment, booking, _paystack.IsSimulated, requiresOtp, displayText);
    }

    /// <summary>
    /// Completes a Mobile Money charge that Paystack paused for an OTP.
    ///
    /// Without this the charge never resolves: polling verify reports it as
    /// pending forever, because Paystack is waiting on the customer's code.
    /// </summary>
    [HttpPost("{reference}/submit-otp")]
    public async Task<ActionResult<PaymentResponse>> SubmitOtp(
        string reference, SubmitOtpRequest req, CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return Unauthorized("Register first.");

        if (string.IsNullOrWhiteSpace(req.Otp))
            return BadRequest("Enter the code you were sent.");

        var payment = await _db.Payments
            .Include(p => p.Booking)
            .FirstOrDefaultAsync(p => p.Reference == reference, ct);
        if (payment is null) return NotFound("Unknown payment reference.");
        if (payment.Booking.StudentUserId != me.Id) return Forbid();

        PaystackChargeResult charge;
        try
        {
            charge = await _paystack.SubmitOtpAsync(reference, req.Otp, ct);
        }
        catch (PaystackException ex)
        {
            _log.LogError(ex, "OTP submission failed for {Reference}.", reference);
            return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
        }

        if (charge.State == PaystackChargeState.Failed)
            return BadRequest(charge.Message ?? "That code was not accepted.");

        // Let Paystack, not the charge response, be the source of truth for money.
        try
        {
            var result = await _paystack.VerifyAsync(reference, ct);
            await _payments.ApplyAsync(payment, payment.Booking, result, ct);
        }
        catch (PaystackException ex)
        {
            _log.LogError(ex, "Post-OTP verification failed for {Reference}.", reference);
        }

        return ToResponse(
            payment,
            payment.Booking,
            _paystack.IsSimulated,
            charge.State == PaystackChargeState.NeedsOtp,
            charge.DisplayText ?? charge.Message);
    }

    /// <summary>
    /// Ask Paystack for the authoritative state of a reference and apply it. Safe to call
    /// repeatedly — a booking already in PaymentHeld just reports back.
    /// </summary>
    [HttpPost("{reference}/verify")]
    public async Task<ActionResult<PaymentResponse>> Verify(string reference, CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return Unauthorized("Register first.");

        var payment = await _db.Payments
            .Include(p => p.Booking)
            .FirstOrDefaultAsync(p => p.Reference == reference, ct);
        if (payment is null) return NotFound("Unknown payment reference.");

        if (payment.Booking.StudentUserId != me.Id && !await IsCompanyStaff(payment.Booking.CompanyId, ct))
            return Forbid();

        string? explanation = null;
        try
        {
            var result = await _paystack.VerifyAsync(reference, ct);
            var (ok, error) = await _payments.ApplyAsync(payment, payment.Booking, result, ct);
            if (!ok) return Conflict(error);

            // A decline is the one thing the student needs explained. Paystack's
            // own wording ("LOW_BALANCE_OR_PAYEE_LIMIT_REACHED_OR_NOT_ALLOWED")
            // is never fit to show, so translate it here.
            if (result.Status == PaymentStatus.Failed)
            {
                explanation = PaymentDeclineMessages.Explain(result.GatewayResponse);
                _log.LogInformation(
                    "Payment {Reference} declined by the provider: {Gateway}",
                    reference, result.GatewayResponse);
            }
        }
        catch (PaystackException ex)
        {
            _log.LogError(ex, "Paystack verify failed for {Reference}.", reference);
            return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
        }

        return ToResponse(
            payment, payment.Booking, _paystack.IsSimulated, displayText: explanation);
    }

    /// <summary>The stored state of a reference (no call to Paystack).</summary>
    [HttpGet("{reference}")]
    public async Task<ActionResult<PaymentResponse>> Get(string reference, CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return Unauthorized("Register first.");

        var payment = await _db.Payments
            .Include(p => p.Booking)
            .FirstOrDefaultAsync(p => p.Reference == reference, ct);
        if (payment is null) return NotFound();

        if (payment.Booking.StudentUserId != me.Id && !await IsCompanyStaff(payment.Booking.CompanyId, ct))
            return Forbid();

        return ToResponse(payment, payment.Booking, _paystack.IsSimulated);
    }

    /// <summary>The payment attached to a booking, if any.</summary>
    [HttpGet("booking/{bookingId:guid}")]
    public async Task<ActionResult<PaymentResponse>> ForBooking(Guid bookingId, CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return Unauthorized("Register first.");

        var payment = await _db.Payments
            .Include(p => p.Booking)
            .FirstOrDefaultAsync(p => p.BookingId == bookingId, ct);
        if (payment is null) return NotFound("No payment for this booking yet.");

        if (payment.Booking.StudentUserId != me.Id && !await IsCompanyStaff(payment.Booking.CompanyId, ct))
            return Forbid();

        return ToResponse(payment, payment.Booking, _paystack.IsSimulated);
    }

    /// <summary>
    /// Paystack server-to-server callback. Signed with HMAC-SHA512 over the raw body using the
    /// secret key — we verify that before trusting anything. Always answer 200 for events we
    /// accept, so Paystack stops retrying.
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var raw = await reader.ReadToEndAsync(ct);

        var signature = Request.Headers["x-paystack-signature"].FirstOrDefault();
        if (!_paystack.IsValidWebhookSignature(raw, signature))
        {
            _log.LogWarning("Rejected a Paystack webhook with an invalid signature.");
            return Unauthorized();
        }

        JsonElement root;
        try
        {
            root = JsonDocument.Parse(raw).RootElement;
        }
        catch (JsonException)
        {
            return BadRequest("Malformed webhook payload.");
        }

        var eventName = root.TryGetProperty("event", out var e) ? e.GetString() : null;
        if (!root.TryGetProperty("data", out var data))
            return Ok();

        var reference = data.TryGetProperty("reference", out var r) ? r.GetString() : null;
        if (string.IsNullOrWhiteSpace(reference))
            return Ok();

        var payment = await _db.Payments
            .Include(p => p.Booking)
            .FirstOrDefaultAsync(p => p.Reference == reference, ct);
        if (payment is null)
        {
            _log.LogWarning("Webhook {Event} for unknown reference {Reference}; ignoring.", eventName, reference);
            return Ok();
        }

        // Don't trust the webhook body for money: re-verify against Paystack.
        try
        {
            var result = await _paystack.VerifyAsync(reference, ct);
            await _payments.ApplyAsync(payment, payment.Booking, result, ct);
        }
        catch (PaystackException ex)
        {
            _log.LogError(ex, "Webhook {Event}: verification of {Reference} failed.", eventName, reference);
            // 502 so Paystack retries later.
            return StatusCode(StatusCodes.Status502BadGateway);
        }

        return Ok();
    }

    private async Task<bool> IsCompanyStaff(Guid companyId, CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return false;
        var company = await _db.Companies.Include(c => c.Members)
            .FirstOrDefaultAsync(c => c.Id == companyId, ct);
        if (company is null) return false;
        return company.OwnerUserId == me.Id || company.Members.Any(m => m.UserId == me.Id);
    }

    private static PaymentResponse ToResponse(
        Payment p,
        Booking b,
        bool simulated,
        bool requiresOtp = false,
        string? displayText = null) => new()
    {
        Reference = p.Reference,
        BookingId = p.BookingId,
        Amount = p.Amount,
        Channel = p.Channel.ToCamel(),
        Status = p.Status.ToCamel(),
        CheckoutUrl = p.CheckoutUrl,
        AuthorizationCode = p.AuthorizationCode,
        BookingStatus = b.Status.ToCamel(),
        CreatedAt = p.CreatedAt,
        Simulated = simulated,
        RequiresOtp = requiresOtp,
        DisplayText = displayText
    };
}
