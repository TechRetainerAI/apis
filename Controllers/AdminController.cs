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
/// Platform-staff endpoints backing the React admin dashboard. The per-user controllers
/// are deliberately "mine"-scoped; these are the cross-tenant reads staff need to find
/// a dispute, an employee, or an unverified listing in the first place.
///
/// Every route here is Admin/Manager only — see <see cref="RequireStaffAsync"/>.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly CurrentUser _current;
    private readonly ILogger<AdminController> _log;

    public AdminController(AppDbContext db, CurrentUser current, ILogger<AdminController> log)
    {
        _db = db;
        _current = current;
        _log = log;
    }

    /// <summary>
    /// Resolves the caller and rejects anyone who isn't platform staff.
    /// Returns the staff user, or an error result to return as-is.
    /// </summary>
    private async Task<(AppUser? user, ActionResult? error)> RequireStaffAsync(CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return (null, Unauthorized("Register first."));
        if (me.Role is not (UserRole.Admin or UserRole.Manager)) return (null, Forbid());
        return (me, null);
    }

    // ---------------------------------------------------------------- stats

    [HttpGet("stats")]
    public async Task<ActionResult<AdminStatsResponse>> Stats(CancellationToken ct)
    {
        var (_, error) = await RequireStaffAsync(ct);
        if (error is not null) return error;

        var heldStates = new[] { BookingStatus.PaymentHeld, BookingStatus.CheckedIn, BookingStatus.Disputed };

        return new AdminStatsResponse
        {
            Users = await _db.Users.CountAsync(ct),
            Students = await _db.Users.CountAsync(u => u.Role == UserRole.Student, ct),
            Staff = await _db.Users.CountAsync(u => u.Role != UserRole.Student, ct),
            Companies = await _db.Companies.CountAsync(ct),
            Hostels = await _db.Hostels.CountAsync(ct),
            UnverifiedHostels = await _db.Hostels.CountAsync(h => !h.IsVerified, ct),
            Bookings = await _db.Bookings.CountAsync(ct),
            OpenDisputes = await _db.Bookings.CountAsync(b => b.Status == BookingStatus.Disputed, ct),
            ReferralsAwaitingPayout =
                await _db.Referrals.CountAsync(r => r.Status == ReferralStatus.Claimed, ct),
            EscrowHeld = await _db.Bookings
                .Where(b => heldStates.Contains(b.Status))
                .SumAsync(b => (int?)b.Amount, ct) ?? 0,
            AwaitingCheckIn = await _db.Bookings
                .CountAsync(b => b.Status == BookingStatus.PaymentHeld, ct),
            StuckPayouts = await _db.Payouts
                .CountAsync(p => p.Status == PayoutStatus.Pending, ct),
            UpcomingEvents = await _db.Events
                .CountAsync(e => e.StartsAt >= DateTime.UtcNow, ct)
        };
    }

    // ------------------------------------------------------------- bookings

    /// <summary>
    /// Every booking on the platform, newest first. <paramref name="status"/> takes a
    /// camelCase <see cref="BookingStatus"/> ("disputed", "paymentHeld", …);
    /// <paramref name="q"/> matches student name/email or hostel name.
    /// </summary>
    [HttpGet("bookings")]
    public async Task<ActionResult<IEnumerable<AdminBookingResponse>>> Bookings(
        [FromQuery] string? status,
        [FromQuery] string? q,
        [FromQuery] int take = 200,
        CancellationToken ct = default)
    {
        var (_, error) = await RequireStaffAsync(ct);
        if (error is not null) return error;

        var query = _db.Bookings.AsNoTracking()
            .Include(b => b.Student)
            .Include(b => b.Hostel).ThenInclude(h => h!.Company)
            .Include(b => b.Room)
            .Include(b => b.Bed)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!TryParseCamel<BookingStatus>(status, out var parsed))
                return BadRequest($"Unknown booking status '{status}'.");
            query = query.Where(b => b.Status == parsed);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(b =>
                b.Student.FullName.Contains(q) ||
                b.Student.Email.Contains(q) ||
                b.Hostel.Name.Contains(q));
        }

        var items = await query
            .OrderByDescending(b => b.CreatedAt)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(ct);

        return items.Select(ToAdminResponse).ToList();
    }

    /// <summary>One booking, with the student/company context support needs.</summary>
    [HttpGet("bookings/{id:guid}")]
    public async Task<ActionResult<AdminBookingResponse>> Booking(Guid id, CancellationToken ct)
    {
        var (_, error) = await RequireStaffAsync(ct);
        if (error is not null) return error;

        var booking = await _db.Bookings.AsNoTracking()
            .Include(b => b.Student)
            .Include(b => b.Hostel).ThenInclude(h => h!.Company)
            .Include(b => b.Room)
            .Include(b => b.Bed)
            .FirstOrDefaultAsync(b => b.Id == id, ct);

        return booking is null ? NotFound() : ToAdminResponse(booking);
    }

    // ---------------------------------------------------------------- users

    /// <summary>Directory of platform users. <paramref name="role"/> is a camelCase role name.</summary>
    [HttpGet("users")]
    public async Task<ActionResult<IEnumerable<AdminUserResponse>>> Users(
        [FromQuery] string? role,
        [FromQuery] string? q,
        [FromQuery] int take = 200,
        CancellationToken ct = default)
    {
        var (_, error) = await RequireStaffAsync(ct);
        if (error is not null) return error;

        var query = _db.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(role))
        {
            if (!TryParseCamel<UserRole>(role, out var parsed))
                return BadRequest($"Unknown role '{role}'.");
            query = query.Where(u => u.Role == parsed);
        }

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(u => u.FullName.Contains(q) || u.Email.Contains(q));

        var items = await query
            .OrderByDescending(u => u.CreatedAt)
            .Take(Math.Clamp(take, 1, 500))
            .Select(u => new AdminUserResponse
            {
                Id = u.Id,
                Email = u.Email,
                Name = u.FullName,
                Phone = u.Phone,
                PhotoUrl = u.PhotoUrl,
                Role = u.Role.ToCamel(),
                IsActive = u.IsActive,
                CreatedAt = u.CreatedAt,
                BookingCount = u.Bookings.Count
            })
            .ToListAsync(ct);

        return items;
    }

    /// <summary>
    /// Set a user's platform role — how an employee becomes a Worker/Manager.
    /// Admin only (a Manager must not be able to mint more Admins), and staff
    /// cannot change their own role.
    /// </summary>
    /// <summary>
    /// Creates an account on someone's behalf — the onboarding path for hostel
    /// owners and their staff, who sign up at MeDan's desk rather than in the
    /// app. The temporary password is returned once, to be handed over in
    /// person; the new manager should change it in Settings after first login.
    /// </summary>
    [HttpPost("users")]
    public async Task<ActionResult<AdminUserResponse>> CreateUser(
        AdminCreateUserRequest req, CancellationToken ct)
    {
        var (me, error) = await RequireStaffAsync(ct);
        if (error is not null) return error;
        if (me!.Role != UserRole.Admin) return Forbid();

        if (!TryParseCamel<UserRole>(req.Role, out var role))
            return BadRequest($"Unknown role '{req.Role}'.");
        // Admin accounts go through the guarded staff-register flow, not here.
        if (role == UserRole.Admin)
            return BadRequest("Create admin accounts via the staff register flow.");

        var email = req.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Email == email, ct))
            return Conflict("An account with that email already exists.");
        if (req.Password.Trim().Length < 8)
            return BadRequest("The temporary password must be at least 8 characters.");

        var user = new AppUser
        {
            Email = email,
            FullName = req.Name.Trim(),
            Phone = req.Phone,
            Role = role,
            PasswordHash = PasswordHasher.Hash(req.Password.Trim())
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("{Admin} created {Role} account {Email}.", me.Email, role, email);
        return new AdminUserResponse
        {
            Id = user.Id,
            Email = user.Email,
            Name = user.FullName,
            Phone = user.Phone,
            PhotoUrl = user.PhotoUrl,
            Role = user.Role.ToCamel(),
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            BookingCount = 0
        };
    }

    [HttpPost("users/{id:guid}/role")]
    public async Task<ActionResult<AdminUserResponse>> SetRole(
        Guid id, SetRoleRequest req, CancellationToken ct)
    {
        var (me, error) = await RequireStaffAsync(ct);
        if (error is not null) return error;
        if (me!.Role != UserRole.Admin) return Forbid();
        if (me.Id == id) return BadRequest("You cannot change your own role.");

        var user = await _db.Users.Include(u => u.Bookings).FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        var previous = user.Role;
        user.Role = req.Role;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _log.LogInformation(
            "User {User} role changed {From} → {To} by {Actor}.", user.Id, previous, req.Role, me.Id);

        return new AdminUserResponse
        {
            Id = user.Id,
            Email = user.Email,
            Name = user.FullName,
            Phone = user.Phone,
            PhotoUrl = user.PhotoUrl,
            Role = user.Role.ToCamel(),
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            BookingCount = user.Bookings.Count
        };
    }

    // ------------------------------------------------------------ referrals

    /// <summary>Referrals across the platform; filter by camelCase status to find payouts due.</summary>
    [HttpGet("referrals")]
    public async Task<ActionResult<IEnumerable<ReferralResponse>>> Referrals(
        [FromQuery] string? status,
        [FromQuery] int take = 200,
        CancellationToken ct = default)
    {
        var (_, error) = await RequireStaffAsync(ct);
        if (error is not null) return error;

        var query = _db.Referrals.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!TryParseCamel<ReferralStatus>(status, out var parsed))
                return BadRequest($"Unknown referral status '{status}'.");
            query = query.Where(r => r.Status == parsed);
        }

        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(ct);

        return items.Select(r => new ReferralResponse
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
        }).ToList();
    }

    // -------------------------------------------------------------- hostels

    /// <summary>Grant or revoke a listing's verified badge.</summary>
    [HttpPost("hostels/{id:guid}/verify")]
    public async Task<IActionResult> SetVerified(Guid id, SetVerifiedRequest req, CancellationToken ct)
    {
        var (me, error) = await RequireStaffAsync(ct);
        if (error is not null) return error;

        var hostel = await _db.Hostels.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (hostel is null) return NotFound();

        hostel.IsVerified = req.Verified;
        await _db.SaveChangesAsync(ct);

        _log.LogInformation(
            "Hostel {Hostel} verified={Verified} by {Actor}.", hostel.Id, req.Verified, me!.Id);
        return NoContent();
    }

    // --------------------------------------------------------------- helpers

    private static AdminBookingResponse ToAdminResponse(Booking b) => new()
    {
        Id = b.Id,
        StudentUserId = b.StudentUserId,
        StudentName = b.Student?.FullName ?? string.Empty,
        StudentEmail = b.Student?.Email ?? string.Empty,
        StudentPhone = b.Student?.Phone,
        HostelId = b.HostelId,
        HostelName = b.Hostel?.Name ?? string.Empty,
        RoomLabel = b.Room?.Label ?? string.Empty,
        BedLabel = b.Bed?.Label ?? string.Empty,
        CompanyId = b.CompanyId,
        CompanyName = b.Hostel?.Company?.Name ?? string.Empty,
        AcademicYear = b.AcademicYear,
        Amount = b.Amount,
        Commission = b.Commission,
        Status = b.Status.ToCamel(),
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

    /// <summary>Parses the camelCase enum names the API emits (e.g. "paymentHeld").</summary>
    private static bool TryParseCamel<TEnum>(string value, out TEnum parsed) where TEnum : struct, Enum =>
        Enum.TryParse(value, ignoreCase: true, out parsed) && Enum.IsDefined(parsed);
}
