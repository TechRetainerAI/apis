using MeDan.Api.Auth;
using MeDan.Api.Data;
using MeDan.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MeDan.Api.Controllers;

public record RegisterDeviceRequest
{
    /// <summary>FCM registration token from the app.</summary>
    public string Token { get; init; } = default!;

    /// <summary>"ios" or "android".</summary>
    public string Platform { get; init; } = "android";
}

public record UnregisterDeviceRequest
{
    public string Token { get; init; } = default!;
}

/// <summary>
/// Where the app tells the API how to reach it.
///
/// The app calls <c>register</c> on every launch and on every FCM token
/// refresh, so this has to be idempotent and cheap.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DevicesController : ControllerBase
{
    /// <summary>Matches the column width configured in AppDbContext.</summary>
    private const int MaxTokenLength = 450;

    private readonly AppDbContext _db;
    private readonly CurrentUser _current;
    private readonly ILogger<DevicesController> _log;

    public DevicesController(AppDbContext db, CurrentUser current, ILogger<DevicesController> log)
    {
        _db = db;
        _current = current;
        _log = log;
    }

    /// <summary>
    /// Points a token at the signed-in user. If the token already exists —
    /// same handset, possibly a different account — it is re-pointed rather
    /// than duplicated, so the previous user stops receiving that device's
    /// notifications.
    /// </summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDeviceRequest req, CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return Unauthorized("Register first.");

        var token = req.Token?.Trim();
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest("A device token is required.");
        if (token.Length > MaxTokenLength)
            return BadRequest($"Device token exceeds {MaxTokenLength} characters.");

        var platform = req.Platform?.Trim().ToLowerInvariant() == "ios"
            ? DevicePlatform.Ios
            : DevicePlatform.Android;

        var existing = await _db.DeviceTokens.FirstOrDefaultAsync(d => d.Token == token, ct);
        if (existing is null)
        {
            _db.DeviceTokens.Add(new DeviceToken
            {
                Token = token,
                UserId = me.Id,
                Platform = platform
            });
        }
        else
        {
            existing.UserId = me.Id;
            existing.Platform = platform;
            existing.LastSeenAt = DateTime.UtcNow;
            existing.DisabledAt = null;   // a live registration proves it works
        }

        await _db.SaveChangesAsync(ct);
        _log.LogDebug("Registered {Platform} push token for {User}.", platform, me.Id);
        return NoContent();
    }

    /// <summary>
    /// Drops a token — call on sign-out so the next person to use the handset
    /// does not receive the previous user's bookings.
    /// </summary>
    [HttpPost("unregister")]
    public async Task<IActionResult> Unregister(UnregisterDeviceRequest req, CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return Unauthorized("Register first.");

        var token = req.Token?.Trim();
        if (string.IsNullOrWhiteSpace(token)) return NoContent();

        // Scoped to the caller so one user cannot unregister another's device.
        var row = await _db.DeviceTokens
            .FirstOrDefaultAsync(d => d.Token == token && d.UserId == me.Id, ct);

        if (row is not null)
        {
            _db.DeviceTokens.Remove(row);
            await _db.SaveChangesAsync(ct);
        }

        return NoContent();
    }
}
