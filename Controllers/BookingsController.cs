using System.Data;
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
/// Booking + escrow lifecycle:
/// reserve a bed (Pending) → payment held (PaymentHeld) → owner confirms arrival (CheckedIn)
/// → after the 48h dispute window, funds release (Completed). Plus cancel.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BookingsController : ControllerBase
{
    private static readonly TimeSpan DisputeWindow = TimeSpan.FromHours(48);

    /// <summary>
    /// States where a student still holds a hostel. Completed/refunded/cancelled
    /// are done with, so they don't block a new booking.
    /// </summary>
    private static readonly BookingStatus[] ActiveStatuses =
    {
        BookingStatus.Pending,
        BookingStatus.PaymentHeld,
        BookingStatus.CheckedIn,
        BookingStatus.Disputed,
    };

    private readonly AppDbContext _db;
    private readonly CurrentUser _current;
    private readonly IPaystackClient _paystack;
    private readonly PaymentService _payments;
    private readonly PayoutService _payouts;
    private readonly ReferralService _referrals;
    private readonly BookingNotifier _notify;
    private readonly ILogger<BookingsController> _log;

    public BookingsController(
        AppDbContext db,
        CurrentUser current,
        IPaystackClient paystack,
        PaymentService payments,
        PayoutService payouts,
        ReferralService referrals,
        BookingNotifier notify,
        ILogger<BookingsController> log)
    {
        _db = db;
        _current = current;
        _paystack = paystack;
        _payments = payments;
        _payouts = payouts;
        _referrals = referrals;
        _notify = notify;
        _log = log;
    }

    /// <summary>The current student's bookings, newest first.</summary>
    [HttpGet("mine")]
    public async Task<ActionResult<IEnumerable<BookingResponse>>> Mine(CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return Unauthorized("Register first.");

        var bookings = await Query().Where(b => b.StudentUserId == me.Id)
            .OrderByDescending(b => b.CreatedAt).ToListAsync(ct);
        return bookings.Select(ToResponse).ToList();
    }

    /// <summary>One booking. Visible to the student who made it and to the hostel's staff.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BookingResponse>> Get(Guid id, CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return Unauthorized("Register first.");

        var booking = await Query().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (booking is null) return NotFound();
        if (booking.StudentUserId != me.Id && !await IsCompanyStaff(booking.CompanyId, ct)) return Forbid();

        return ToResponse(booking);
    }

    /// <summary>Bookings across a company's hostels — the owner/worker dashboard feed.</summary>
    [HttpGet("company/{companyId:guid}")]
    public async Task<ActionResult<IEnumerable<BookingResponse>>> ForCompany(
        Guid companyId, [FromQuery] BookingStatus? status, CancellationToken ct)
    {
        if (!await IsCompanyStaff(companyId, ct)) return Forbid();

        var q = Query().Where(b => b.CompanyId == companyId);
        if (status is BookingStatus s) q = q.Where(b => b.Status == s);

        var bookings = await q.OrderByDescending(b => b.CreatedAt).ToListAsync(ct);
        return bookings.Select(ToResponse).ToList();
    }

    /// <summary>
    /// The student's current hostel — the one booking that is still live. Returns
    /// 204 when they have none, so the app can tell "no hostel yet" apart from an error.
    /// </summary>
    [HttpGet("current")]
    public async Task<ActionResult<BookingResponse>> Current(CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return Unauthorized("Register first.");

        var booking = await Query()
            .Where(b => b.StudentUserId == me.Id && ActiveStatuses.Contains(b.Status))
            .OrderByDescending(b => b.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return booking is null ? NoContent() : ToResponse(booking);
    }

    /// <summary>Reserve a bed in a room (status Pending, bed held). Picks first free bed if none given.</summary>
    [HttpPost]
    public async Task<ActionResult<BookingResponse>> Create(CreateBookingRequest req, CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return Unauthorized("Register first.");

        // A student lives in one place at a time. Without this a student could
        // hold beds in several hostels at once, blocking them for everyone else.
        var existing = await _db.Bookings
            .Include(b => b.Hostel)
            .FirstOrDefaultAsync(
                b => b.StudentUserId == me.Id && ActiveStatuses.Contains(b.Status), ct);

        if (existing is not null)
            return Conflict(
                $"You already have a hostel: {existing.Hostel?.Name ?? "a booking"} " +
                $"({existing.Status.ToCamel()}). Cancel it before booking another.");

        // Serializable transaction prevents two students grabbing the same bed.
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var room = await _db.Rooms.Include(r => r.Hostel)
            .FirstOrDefaultAsync(r => r.Id == req.RoomId, ct);
        if (room is null) return NotFound("Room not found.");

        var bed = req.BedId is Guid bedId
            ? await _db.Beds.FirstOrDefaultAsync(x => x.Id == bedId && x.RoomId == room.Id, ct)
            : await _db.Beds.FirstOrDefaultAsync(x => x.RoomId == room.Id && x.Status == BedStatus.Available, ct);

        if (bed is null) return NotFound("No bed available in this room.");
        if (bed.Status != BedStatus.Available) return Conflict("That bed is no longer available.");

        var company = await _db.Companies.FirstAsync(c => c.Id == room.Hostel.CompanyId, ct);
        var amount = room.PricePerBedPerSemester;
        var commission = (int)Math.Round(amount * company.CommissionRate, MidpointRounding.AwayFromZero);

        var booking = new Booking
        {
            StudentUserId = me.Id,
            HostelId = room.HostelId,
            RoomId = room.Id,
            BedId = bed.Id,
            CompanyId = company.Id,
            AcademicYear = req.AcademicYear,
            Amount = amount,
            Commission = commission,
            Status = BookingStatus.Pending,
            CheckInCode = GenerateCode()
        };

        bed.Status = BedStatus.Reserved;
        bed.CurrentBookingId = booking.Id;
        room.AvailableBeds = Math.Max(0, room.AvailableBeds - 1);
        if (room.AvailableBeds == 0) room.Status = RoomStatus.Occupied;

        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var saved = await Query().FirstAsync(b => b.Id == booking.Id, ct);
        return CreatedAtAction(nameof(Mine), null, ToResponse(saved));
    }

    /// <summary>
    /// Confirm a Paystack reference for this booking and hold the funds in escrow.
    /// The reference is <b>verified against Paystack</b> before anything moves — the client
    /// saying "it paid" is not enough. Prefer <c>POST /api/payments/initialize</c> +
    /// <c>/verify</c>; this endpoint stays for clients that already hold a reference.
    /// </summary>
    [HttpPost("{id:guid}/confirm-payment")]
    public async Task<ActionResult<BookingResponse>> ConfirmPayment(Guid id, ConfirmPaymentRequest req, CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        var booking = await Query().Include(b => b.Payment).FirstOrDefaultAsync(b => b.Id == id, ct);
        if (booking is null) return NotFound();
        if (me is null || booking.StudentUserId != me.Id) return Forbid();
        if (booking.Status != BookingStatus.Pending) return Conflict($"Cannot pay a booking in state {booking.Status}.");

        var payment = booking.Payment;
        if (payment is not null && payment.Reference != req.PaystackReference)
            return Conflict("A different payment reference is already attached to this booking.");

        PaystackVerifyResult result;
        try
        {
            result = await _paystack.VerifyAsync(req.PaystackReference, ct);
        }
        catch (PaystackException ex)
        {
            _log.LogError(ex, "Verification of {Reference} failed for booking {Booking}.", req.PaystackReference, id);
            return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
        }

        if (result.Status != PaymentStatus.Success)
            return Conflict($"Paystack reports this payment as {result.Status.ToCamel()}.");

        // The reference may have been created outside /api/payments/initialize.
        if (payment is null)
        {
            payment = new Payment
            {
                Reference = req.PaystackReference,
                BookingId = booking.Id,
                Amount = booking.Amount,
                Channel = result.Channel,
                Status = PaymentStatus.Initialized
            };
            _db.Payments.Add(payment);
        }

        var (ok, error) = await _payments.ApplyAsync(payment, booking, result, ct);
        if (!ok) return Conflict(error);

        return ToResponse(booking);
    }

    /// <summary>Owner/worker confirms the student's arrival with the check-in code. Opens the dispute window.</summary>
    [HttpPost("{id:guid}/check-in")]
    public async Task<ActionResult<BookingResponse>> CheckIn(Guid id, CheckInRequest req, CancellationToken ct)
    {
        var booking = await Query().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (booking is null) return NotFound();
        if (!await IsCompanyStaff(booking.CompanyId, ct)) return Forbid();
        if (booking.Status != BookingStatus.PaymentHeld) return Conflict($"Cannot check in a booking in state {booking.Status}.");
        if (!string.Equals(booking.CheckInCode, req.CheckInCode, StringComparison.OrdinalIgnoreCase))
            return BadRequest("Invalid check-in code.");

        booking.Status = BookingStatus.CheckedIn;
        booking.CheckedInAt = DateTime.UtcNow;

        var bed = await _db.Beds.FirstAsync(b => b.Id == booking.BedId, ct);
        bed.Status = BedStatus.Occupied;

        await _db.SaveChangesAsync(ct);
        await _notify.CheckedInAsync(booking, ct);
        return ToResponse(booking);
    }

    /// <summary>
    /// The other students sharing this room.
    ///
    /// Scoped to the caller's own booking — you can only see who you are
    /// actually living with, and only while your booking is live. Cancelled
    /// and refunded bookings are excluded so a bed that fell through does not
    /// show a phantom roommate.
    /// </summary>
    [HttpGet("{id:guid}/roommates")]
    public async Task<ActionResult<IEnumerable<RoommateResponse>>> Roommates(
        Guid id, CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return Unauthorized("Register first.");

        var booking = await _db.Bookings.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id, ct);
        if (booking is null) return NotFound();

        // Only the student themselves, or the hostel's staff, may look.
        if (booking.StudentUserId != me.Id && !await IsCompanyStaff(booking.CompanyId, ct))
            return Forbid();

        var live = new[]
        {
            BookingStatus.PaymentHeld, BookingStatus.CheckedIn, BookingStatus.Completed
        };

        var mates = await _db.Bookings.AsNoTracking()
            .Include(b => b.Student).ThenInclude(u => u!.StudentProfile)
            .Include(b => b.Bed)
            .Where(b => b.RoomId == booking.RoomId
                        && b.Id != booking.Id
                        && b.AcademicYear == booking.AcademicYear
                        && live.Contains(b.Status))
            .ToListAsync(ct);

        return mates.Select(b => new RoommateResponse
        {
            UserId = b.StudentUserId,
            Name = b.Student?.FullName ?? "Student",
            PhotoUrl = b.Student?.PhotoUrl,
            Course = b.Student?.StudentProfile?.Course,
            Level = b.Student?.StudentProfile?.Level,
            BedLabel = b.Bed?.Label ?? "",
            HasCheckedIn = b.Status is BookingStatus.CheckedIn or BookingStatus.Completed
        }).ToList();
    }

    /// <summary>Release escrow to the owner after the 48h dispute window has elapsed.</summary>
    [HttpPost("{id:guid}/complete")]
    public async Task<ActionResult<BookingResponse>> Complete(Guid id, CancellationToken ct)
    {
        var booking = await Query().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (booking is null) return NotFound();
        if (!await IsCompanyStaff(booking.CompanyId, ct)) return Forbid();
        if (booking.Status != BookingStatus.CheckedIn) return Conflict($"Cannot complete a booking in state {booking.Status}.");
        if (booking.CheckedInAt is null || DateTime.UtcNow - booking.CheckedInAt < DisputeWindow)
            return BadRequest("The 48-hour dispute window has not yet closed.");

        booking.Status = BookingStatus.Completed;
        booking.CompletedAt = DateTime.UtcNow;

        // A completed first booking is what unlocks the referrer's GH₵ reward.
        await _referrals.GrantRewardIfEligibleAsync(booking.StudentUserId, booking.Id, ct);

        await _db.SaveChangesAsync(ct);

        // Escrow leaves MeDan here. Idempotent, so a retried request cannot pay twice.
        await _payouts.ReleaseAsync(booking, ct);

        return ToResponse(booking);
    }

    /// <summary>
    /// Student raises a dispute while the money is still in escrow (paid, or checked in and
    /// inside the 48h window). Freezes the release until support resolves it.
    /// </summary>
    [HttpPost("{id:guid}/dispute")]
    public async Task<ActionResult<BookingResponse>> Dispute(Guid id, RaiseDisputeRequest req, CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        var booking = await Query().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (booking is null) return NotFound();
        if (me is null || booking.StudentUserId != me.Id) return Forbid();
        if (booking.Status is not (BookingStatus.PaymentHeld or BookingStatus.CheckedIn))
            return Conflict($"Cannot dispute a booking in state {booking.Status}.");
        if (booking.Status == BookingStatus.CheckedIn &&
            booking.CheckedInAt is DateTime t && DateTime.UtcNow - t > DisputeWindow)
            return BadRequest("The 48-hour dispute window has closed.");

        booking.Status = BookingStatus.Disputed;
        booking.DisputeReason = req.Reason;
        booking.DisputedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        _log.LogWarning("Booking {Booking} disputed by student {Student}.", booking.Id, me.Id);
        return ToResponse(booking);
    }

    /// <summary>
    /// Support closes a dispute: refund the student (bed goes back on the market) or release
    /// escrow to the owner. Platform staff only.
    /// </summary>
    /// <remarks>
    /// State only — the Paystack refund/transfer legs are not wired yet, so record the payout
    /// you made manually in <see cref="ResolveDisputeRequest.Note"/>.
    /// </remarks>
    [HttpPost("{id:guid}/resolve-dispute")]
    public async Task<ActionResult<BookingResponse>> ResolveDispute(
        Guid id, ResolveDisputeRequest req, CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return Unauthorized("Register first.");
        if (me.Role is not (UserRole.Admin or UserRole.Manager)) return Forbid();

        var booking = await Query().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (booking is null) return NotFound();
        if (booking.Status != BookingStatus.Disputed)
            return Conflict($"Booking is not disputed (state {booking.Status}).");

        booking.DisputeResolution = req.Note;
        booking.ResolvedAt = DateTime.UtcNow;

        var refunding = req.Outcome == DisputeOutcome.Refund;
        if (refunding)
        {
            booking.Status = BookingStatus.Refunded;
            await ReleaseBedAsync(booking, ct);
        }
        else
        {
            booking.Status = BookingStatus.Completed;
            booking.CompletedAt = DateTime.UtcNow;
            await _referrals.GrantRewardIfEligibleAsync(booking.StudentUserId, booking.Id, ct);
        }

        await _db.SaveChangesAsync(ct);

        // Move the money to match the decision.
        if (refunding) await _payouts.RefundAsync(booking, ct);
        else await _payouts.ReleaseAsync(booking, ct);

        // Tell the student which way it went — this is the message they have
        // been waiting on since they raised the dispute.
        if (refunding) await _notify.RefundedAsync(booking, ct);
        else await _notify.ReleasedAsync(booking, ct);

        _log.LogInformation(
            "Dispute on booking {Booking} resolved as {Outcome} by {User}.", booking.Id, req.Outcome, me.Id);
        return ToResponse(booking);
    }

    /// <summary>Student cancels a not-yet-checked-in booking; the bed is released.</summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<BookingResponse>> Cancel(Guid id, CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        var booking = await Query().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (booking is null) return NotFound();
        if (me is null || booking.StudentUserId != me.Id) return Forbid();
        if (booking.Status is not (BookingStatus.Pending or BookingStatus.PaymentHeld))
            return Conflict($"Cannot cancel a booking in state {booking.Status}.");

        booking.Status = BookingStatus.Cancelled;
        await ReleaseBedAsync(booking, ct);

        await _db.SaveChangesAsync(ct);
        return ToResponse(booking);
    }

    // ---- helpers ----

    /// <summary>Puts the bed back on the market when a booking ends without a stay.</summary>
    private async Task ReleaseBedAsync(Booking booking, CancellationToken ct)
    {
        var bed = await _db.Beds.FirstAsync(b => b.Id == booking.BedId, ct);
        bed.Status = BedStatus.Available;
        bed.CurrentBookingId = null;

        var room = await _db.Rooms.FirstAsync(r => r.Id == booking.RoomId, ct);
        room.AvailableBeds += 1;
        if (room.Status == RoomStatus.Occupied) room.Status = RoomStatus.Available;
    }

    // Photos are included so the response can carry the hostel's cover image —
    // the app shows it on booking cards without a second round trip.
    private IQueryable<Booking> Query() => _db.Bookings
        .Include(b => b.Hostel).ThenInclude(h => h.Photos)
        .Include(b => b.Room).Include(b => b.Bed);

    private async Task<bool> IsCompanyStaff(Guid companyId, CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return false;
        var company = await _db.Companies.Include(c => c.Members)
            .FirstOrDefaultAsync(c => c.Id == companyId, ct);
        if (company is null) return false;
        return company.OwnerUserId == me.Id || company.Members.Any(m => m.UserId == me.Id);
    }

    private static string GenerateCode() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private static BookingResponse ToResponse(Booking b) => new()
    {
        Id = b.Id,
        HostelId = b.HostelId,
        HostelName = b.Hostel?.Name ?? string.Empty,
        HostelPhotoUrl = b.Hostel?.Photos
            .OrderByDescending(p => p.IsCover).ThenBy(p => p.SortOrder)
            .Select(p => p.Url).FirstOrDefault(),
        RoomId = b.RoomId,
        RoomLabel = b.Room?.Label ?? string.Empty,
        BedId = b.BedId,
        BedLabel = b.Bed?.Label ?? string.Empty,
        AcademicYear = b.AcademicYear,
        Amount = b.Amount,
        Commission = b.Commission,
        Status = b.Status.ToCamel(),
        CheckInCode = b.CheckInCode,
        PaystackReference = b.PaystackReference,
        DisputeReason = b.DisputeReason,
        DisputeResolution = b.DisputeResolution,
        CreatedAt = b.CreatedAt,
        PaidAt = b.PaidAt,
        CheckedInAt = b.CheckedInAt,
        CompletedAt = b.CompletedAt,
        DisputedAt = b.DisputedAt,
        ResolvedAt = b.ResolvedAt
    };
}
