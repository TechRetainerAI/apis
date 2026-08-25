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
    private readonly EmailSender _email;
    private readonly ILogger<AuthController> _log;

    public AuthController(
        AppDbContext db,
        CurrentUser current,
        IImageStorage images,
        TokenService tokens,
        EmailSender email,
        ILogger<AuthController> log)
    {
        _db = db;
        _current = current;
        _images = images;
        _tokens = tokens;
        _email = email;
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

        var code = IssueOtp(user);
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        await _email.SendOtpAsync(user.Email, user.FullName, code, ct);
        _log.LogInformation("Account {Email} registered; verification code sent.", user.Email);

        return Accepted(new VerificationPendingResponse
        {
            Email = user.Email,
            Message = "We sent a 6-digit code to your email. Enter it to finish signing up."
        });
    }

    /// <summary>
    /// Confirms the OTP emailed at registration and returns the signed token.
    /// </summary>
    [HttpPost("verify-email")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> VerifyEmail(VerifyEmailRequest req, CancellationToken ct)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await _db.Users
            .Include(u => u.StudentProfile)
            .FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null) return Unauthorized("Incorrect code.");

        if (user.EmailVerifiedAt is not null) return Issue(user); // already verified — idempotent

        if (user.EmailOtpHash is null || user.EmailOtpExpiresAt < DateTime.UtcNow)
            return BadRequest("That code has expired — request a new one.");

        if (user.EmailOtpAttempts >= 5)
            return BadRequest("Too many attempts — request a new code.");

        if (!PasswordHasher.Verify(req.Code, user.EmailOtpHash))
        {
            user.EmailOtpAttempts++;
            await _db.SaveChangesAsync(ct);
            return Unauthorized("Incorrect code.");
        }

        user.EmailVerifiedAt = DateTime.UtcNow;
        user.EmailOtpHash = null;
        user.EmailOtpExpiresAt = null;
        user.EmailOtpAttempts = 0;
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Email verified for {Email}.", user.Email);

        return Issue(user);
    }

    /// <summary>Emails a fresh OTP to an unverified account.</summary>
    [HttpPost("resend-code")]
    [AllowAnonymous]
    public async Task<IActionResult> ResendCode(ResendCodeRequest req, CancellationToken ct)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        // Same response whether or not the account exists or is already verified —
        // never reveal which emails are registered.
        if (user is not null && user.EmailVerifiedAt is null)
        {
            var code = IssueOtp(user);
            await _db.SaveChangesAsync(ct);
            await _email.SendOtpAsync(user.Email, user.FullName, code, ct);
        }
        return Accepted(new { message = "If that account needs verification, a code is on its way." });
    }

    /// <summary>
    /// Emails a password-reset code. Same response whether or not the account
    /// exists — never reveal which emails are registered.
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest req, CancellationToken ct)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is not null && user.PasswordHash is not null)
        {
            var code = IssueOtp(user);
            await _db.SaveChangesAsync(ct);
            await _email.SendOtpAsync(user.Email, user.FullName, code, ct);
            _log.LogInformation("Password-reset code sent to {Email}.", user.Email);
        }
        return Accepted(new { message = "If that account exists, a reset code is on its way." });
    }

    /// <summary>Confirms the reset code and sets the new password.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest req, CancellationToken ct)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null) return Unauthorized("Incorrect code.");

        if (user.EmailOtpHash is null || user.EmailOtpExpiresAt < DateTime.UtcNow)
            return BadRequest("That code has expired — request a new one.");
        if (user.EmailOtpAttempts >= 5)
            return BadRequest("Too many attempts — request a new code.");

        if (!PasswordHasher.Verify(req.Code, user.EmailOtpHash))
        {
            user.EmailOtpAttempts++;
            await _db.SaveChangesAsync(ct);
            return Unauthorized("Incorrect code.");
        }

        user.PasswordHash = PasswordHasher.Hash(req.NewPassword);
        // Entering the emailed code proves ownership, so an unverified account
        // that resets its password is verified by the same act.
        user.EmailVerifiedAt ??= DateTime.UtcNow;
        user.EmailOtpHash = null;
        user.EmailOtpExpiresAt = null;
        user.EmailOtpAttempts = 0;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Password reset for {Email}.", user.Email);

        return NoContent();
    }

    /// <summary>Stamps a fresh OTP onto the user; caller saves and emails it.</summary>
    private static string IssueOtp(AppUser user)
    {
        var code = System.Security.Cryptography.RandomNumberGenerator
            .GetInt32(0, 1_000_000).ToString("D6");
        user.EmailOtpHash = PasswordHasher.Hash(code);
        user.EmailOtpExpiresAt = DateTime.UtcNow.AddMinutes(10);
        user.EmailOtpAttempts = 0;
        return code;
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

        // Students must verify their email before first sign-in. A fresh code is
        // sent here so the app can drop them straight onto the OTP screen.
        if (user.Role == UserRole.Student && user.EmailVerifiedAt is null)
        {
            var code = IssueOtp(user);
            await _db.SaveChangesAsync(ct);
            await _email.SendOtpAsync(user.Email, user.FullName, code, ct);
            return StatusCode(StatusCodes.Status403Forbidden, new VerificationPendingResponse
            {
                Email = user.Email,
                Message = "Verify your email first — we just sent you a new code."
            });
        }

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
