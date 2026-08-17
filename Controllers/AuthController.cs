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
/// Account identity for app users. Credentials live in this API (PBKDF2 hash);
/// sign-up and sign-in both return a MeDan-signed JWT that every other endpoint accepts.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize] // except where [AllowAnonymous] — register/login
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly CurrentUser _current;
    private readonly IImageStorage _images;
    private readonly TokenService _tokens;
    private readonly ILogger<AuthController> _log;

    public AuthController(
        AppDbContext db,
        CurrentUser current,
        IImageStorage images,
        TokenService tokens,
        ILogger<AuthController> log)
    {
        _db = db;
        _current = current;
        _images = images;
        _tokens = tokens;
        _log = log;
    }

    /// <summary>
    /// Sign up a student and return a signed token. Always creates a Student account —
    /// elevated roles are granted by an Admin through the dashboard.
    /// Student details are optional here so a partly-filled profile can be completed later.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest req, CancellationToken ct)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Email == email, ct))
            return Conflict("An account with that email already exists.");

        var user = new AppUser
        {
            Email = email,
            FullName = req.Name.Trim(),
            Phone = req.Phone,
            Role = UserRole.Student,
            PasswordHash = PasswordHasher.Hash(req.Password)
        };

        if (req.Student is { } s)
        {
            user.StudentProfile = new StudentProfile
            {
                Course = s.Course,
                Department = s.Department,
                Level = s.Level,
                CampusCode = s.CampusCode,
                IndexNumber = s.IndexNumber,
                GuardianName = s.GuardianName,
                GuardianPhone = s.GuardianPhone,
                GuardianRelationship = s.GuardianRelationship,
                GuardianEmail = s.GuardianEmail
            };
        }

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Account {Email} registered.", user.Email);

        return Issue(user);
    }

    /// <summary>Exchange email + password for a signed token.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest req, CancellationToken ct)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await _db.Users
            .Include(u => u.StudentProfile)
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        // One message for "no such account" and "wrong password" — never reveal
        // which emails are registered.
        if (user is null || !PasswordHasher.Verify(req.Password, user.PasswordHash))
        {
            _log.LogWarning("Failed login for {Email}.", email);
            return Unauthorized("Incorrect email or password.");
        }

        if (!user.IsActive) return Unauthorized("This account is disabled.");

        return Issue(user);
    }

    private ActionResult<AuthResponse> Issue(AppUser user)
    {
        var (token, expiresAt) = _tokens.Issue(user);
        return new AuthResponse { Token = token, ExpiresAt = expiresAt, User = ToResponse(user) };
    }

    /// <summary>Returns the current user's profile.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<UserResponse>> Me(CancellationToken ct)
    {
        var user = await _current.GetAsync(includeStudent: true, ct);
        return user is null ? NotFound("Not registered.") : ToResponse(user);
    }

    /// <summary>Update the caller's own name and phone.</summary>
    [HttpPut("me")]
    public async Task<ActionResult<UserResponse>> UpdateMe(
        UpdateProfileRequest req, CancellationToken ct)
    {
        var user = await _current.GetAsync(includeStudent: true, ct);
        if (user is null) return NotFound("Not registered.");

        user.FullName = req.Name.Trim();
        user.Phone = string.IsNullOrWhiteSpace(req.Phone) ? null : req.Phone.Trim();
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return ToResponse(user);
    }

    /// <summary>Change the caller's password. The current one must be supplied.</summary>
    [HttpPost("me/password")]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordRequest req, CancellationToken ct)
    {
        var user = await _current.GetAsync(ct: ct);
        if (user is null) return NotFound("Not registered.");

        if (!PasswordHasher.Verify(req.CurrentPassword, user.PasswordHash))
            return BadRequest("Your current password is incorrect.");

        user.PasswordHash = PasswordHasher.Hash(req.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Password changed for {User}.", user.Id);
        return NoContent();
    }

    /// <summary>
    /// Upload/replace the current user's profile photo (any user, incl. students).
    /// Send as multipart/form-data with a single "file" field.
    /// </summary>
    [HttpPost("me/photo")]
    public async Task<ActionResult<UserResponse>> UploadPhoto(IFormFile file, CancellationToken ct)
    {
        var user = await _current.GetAsync(includeStudent: true, ct);
        if (user is null) return NotFound("Not registered.");

        string url;
        try { url = await _images.SaveAsync(file, "avatars", ct); }
        catch (InvalidImageException ex) { return BadRequest(ex.Message); }

        _images.Delete(user.PhotoUrl); // remove the previous avatar, if any
        user.PhotoUrl = url;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return ToResponse(user);
    }

    private static UserResponse ToResponse(AppUser u) => new()
    {
        Id = u.Id,
        Email = u.Email,
        Name = u.FullName,
        Phone = u.Phone,
        PhotoUrl = u.PhotoUrl,
        Role = u.Role.ToCamel(),
        Student = u.StudentProfile is null ? null : new StudentInfo
        {
            Course = u.StudentProfile.Course,
            Department = u.StudentProfile.Department,
            Level = u.StudentProfile.Level,
            CampusCode = u.StudentProfile.CampusCode,
            IndexNumber = u.StudentProfile.IndexNumber,
            GuardianName = u.StudentProfile.GuardianName,
            GuardianPhone = u.StudentProfile.GuardianPhone,
            GuardianRelationship = u.StudentProfile.GuardianRelationship,
            GuardianEmail = u.StudentProfile.GuardianEmail
        }
    };
}
