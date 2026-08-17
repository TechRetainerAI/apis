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
/// Sign-in for platform staff. Unlike the mobile app — whose users live in Firebase —
/// staff accounts are owned by this API: the password is stored here as a PBKDF2 hash
/// and login returns a JWT this API signs itself.
/// </summary>
[ApiController]
[Route("api/admin/auth")]
public class AdminAuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly CurrentUser _current;
    private readonly TokenService _tokens;
    private readonly ILogger<AdminAuthController> _log;

    public AdminAuthController(
        AppDbContext db, CurrentUser current, TokenService tokens, ILogger<AdminAuthController> log)
    {
        _db = db;
        _current = current;
        _tokens = tokens;
        _log = log;
    }

    /// <summary>
    /// Creates a staff account.
    ///
    /// Bootstrap: while no staff account exists, this is open and the first account
    /// created becomes <see cref="UserRole.Admin"/> — that's how a brand-new deployment
    /// gets its first administrator. Once one exists the endpoint closes: only a
    /// signed-in Admin may create further staff.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<StaffAuthResponse>> Register(StaffRegisterRequest req, CancellationToken ct)
    {
        var staffRoles = new[] { UserRole.Admin, UserRole.Manager };
        var bootstrap = !await _db.Users.AnyAsync(u => staffRoles.Contains(u.Role), ct);

        UserRole role;
        if (bootstrap)
        {
            // Nobody can authorise this yet, so the very first account is the Admin.
            role = UserRole.Admin;
        }
        else
        {
            var me = await _current.GetAsync(ct: ct);
            if (me is null) return Unauthorized("Staff already exist — sign in as an Admin to add more.");
            if (me.Role != UserRole.Admin) return Forbid();
            role = req.Role;
        }

        var email = req.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Email == email, ct))
            return Conflict("A user with that email already exists.");

        var user = new AppUser
        {
            Email = email,
            FullName = req.Name.Trim(),
            Phone = req.Phone,
            Role = role,
            PasswordHash = PasswordHasher.Hash(req.Password)
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation(
            "Staff account {Email} created with role {Role} (bootstrap: {Bootstrap}).",
            user.Email, role, bootstrap);

        return Issue(user);
    }

    /// <summary>Exchanges staff credentials for a MeDan JWT.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<StaffAuthResponse>> Login(StaffLoginRequest req, CancellationToken ct)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        // One message for "no such user", "Firebase-only user", and "wrong password" —
        // never reveal which accounts exist or how they authenticate.
        if (user is null || !PasswordHasher.Verify(req.Password, user.PasswordHash))
        {
            _log.LogWarning("Failed staff login for {Email}.", email);
            return Unauthorized("Incorrect email or password.");
        }

        if (!user.IsActive) return Unauthorized("This account is disabled.");

        return Issue(user);
    }

    /// <summary>The signed-in staff user — lets the dashboard validate a stored token on load.</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserResponse>> Me(CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        return me is null ? NotFound("Not registered.") : ToResponse(me);
    }

    private ActionResult<StaffAuthResponse> Issue(AppUser user)
    {
        var (token, expiresAt) = _tokens.Issue(user);
        return new StaffAuthResponse { Token = token, ExpiresAt = expiresAt, User = ToResponse(user) };
    }

    private static UserResponse ToResponse(AppUser u) => new()
    {
        Id = u.Id,
        Email = u.Email,
        Name = u.FullName,
        Phone = u.Phone,
        PhotoUrl = u.PhotoUrl,
        Role = u.Role.ToCamel()
    };
}
