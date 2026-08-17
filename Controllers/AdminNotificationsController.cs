using MeDan.Api.Auth;
using MeDan.Api.Data;
using MeDan.Api.Models;
using MeDan.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MeDan.Api.Controllers;

/// <summary>Who a staff notification goes to.</summary>
public enum NotificationAudience
{
    /// <summary>A single user, picked by id.</summary>
    User = 0,

    /// <summary>Everyone holding a given role.</summary>
    Role = 1,

    /// <summary>Every user with at least one live device.</summary>
    Everyone = 2
}

public record SendNotificationRequest
{
    public NotificationAudience Audience { get; init; } = NotificationAudience.User;

    /// <summary>Required when <see cref="Audience"/> is User.</summary>
    public Guid? UserId { get; init; }

    /// <summary>Required when <see cref="Audience"/> is Role. camelCase, e.g. "student".</summary>
    public string? Role { get; init; }

    public string Title { get; init; } = default!;
    public string Body { get; init; } = default!;

    /// <summary>Optional in-app route to open on tap, e.g. "/bookings".</summary>
    public string? Route { get; init; }

    /// <summary>Poster URL from <c>POST /api/admin/notifications/image</c>.</summary>
    public string? ImageUrl { get; init; }
}

public record SendNotificationResponse
{
    public int Recipients { get; init; }
    public int Devices { get; init; }

    /// <summary>False when no Firebase service account is configured — nothing was sent.</summary>
    public bool Delivered { get; init; }

    public string Message { get; init; } = default!;
}

public record NotificationReachResponse
{
    public int TotalUsers { get; init; }
    public int ReachableUsers { get; init; }
    public int Devices { get; init; }
    public int AndroidDevices { get; init; }
    public int IosDevices { get; init; }
    public bool PushConfigured { get; init; }
}

/// <summary>
/// Lets staff push a message to students from the dashboard — maintenance
/// windows, campus notices, "your hostel has been verified".
///
/// Deliberately not a general broadcast tool: every send is attributed in the
/// log, and the audience is limited to a single user, a role, or everyone, so
/// there is no way to accidentally target a half-built segment.
/// </summary>
[ApiController]
[Route("api/admin/notifications")]
[Authorize]
public class AdminNotificationsController : ControllerBase
{
    private const int MaxTitle = 80;
    private const int MaxBody = 500;

    private readonly AppDbContext _db;
    private readonly CurrentUser _current;
    private readonly PushSender _push;
    private readonly IImageStorage _images;
    private readonly ILogger<AdminNotificationsController> _log;

    public AdminNotificationsController(
        AppDbContext db,
        CurrentUser current,
        PushSender push,
        IImageStorage images,
        ILogger<AdminNotificationsController> log)
    {
        _db = db;
        _current = current;
        _push = push;
        _images = images;
        _log = log;
    }

    private async Task<(AppUser? user, ActionResult? error)> RequireStaffAsync(CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return (null, Unauthorized("Register first."));
        if (me.Role is not (UserRole.Admin or UserRole.Manager)) return (null, Forbid());
        return (me, null);
    }

    /// <summary>
    /// How many people a broadcast would actually reach. Shown before sending
    /// so staff are not guessing at the size of what they are about to do.
    /// </summary>
    [HttpGet("reach")]
    public async Task<ActionResult<NotificationReachResponse>> Reach(CancellationToken ct)
    {
        var (_, error) = await RequireStaffAsync(ct);
        if (error is not null) return error;

        var live = _db.DeviceTokens.Where(d => d.DisabledAt == null);

        return new NotificationReachResponse
        {
            TotalUsers = await _db.Users.CountAsync(ct),
            ReachableUsers = await live.Select(d => d.UserId).Distinct().CountAsync(ct),
            Devices = await live.CountAsync(ct),
            AndroidDevices = await live.CountAsync(d => d.Platform == DevicePlatform.Android, ct),
            IosDevices = await live.CountAsync(d => d.Platform == DevicePlatform.Ios, ct),
            PushConfigured = _push.IsConfigured
        };
    }

    /// <summary>
    /// Hosts a poster for an upcoming notification. Returns the URL to put in
    /// <see cref="SendNotificationRequest.ImageUrl"/> — uploading first means a
    /// failed image never blocks the send itself.
    /// </summary>
    [HttpPost("image")]
    public async Task<ActionResult<object>> UploadImage(IFormFile file, CancellationToken ct)
    {
        var (_, error) = await RequireStaffAsync(ct);
        if (error is not null) return error;

        try
        {
            var url = await _images.SaveAsync(file, "notices", ct);
            return new { url };
        }
        catch (InvalidImageException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Sends a notification to the chosen audience.</summary>
    [HttpPost("send")]
    public async Task<ActionResult<SendNotificationResponse>> Send(
        SendNotificationRequest req, CancellationToken ct)
    {
        var (me, error) = await RequireStaffAsync(ct);
        if (error is not null) return error;

        var title = req.Title?.Trim();
        var body = req.Body?.Trim();

        if (string.IsNullOrWhiteSpace(title)) return BadRequest("A title is required.");
        if (string.IsNullOrWhiteSpace(body)) return BadRequest("A message is required.");
        if (title.Length > MaxTitle)
            return BadRequest($"Title must be {MaxTitle} characters or fewer.");
        if (body.Length > MaxBody)
            return BadRequest($"Message must be {MaxBody} characters or fewer.");

        // Resolve the audience to concrete user ids so the count reported back
        // is the real one rather than an estimate.
        List<Guid> userIds;
        string audienceLabel;

        switch (req.Audience)
        {
            case NotificationAudience.User:
                if (req.UserId is not { } userId)
                    return BadRequest("A userId is required when sending to one user.");
                if (!await _db.Users.AnyAsync(u => u.Id == userId, ct))
                    return NotFound("User not found.");
                userIds = [userId];
                audienceLabel = $"user {userId}";
                break;

            case NotificationAudience.Role:
                if (!TryParseRole(req.Role, out var role))
                    return BadRequest($"Unknown role '{req.Role}'.");
                userIds = await _db.Users.Where(u => u.Role == role)
                    .Select(u => u.Id).ToListAsync(ct);
                audienceLabel = $"role {role}";
                break;

            default:
                // Every user. When push was the only channel this was limited
                // to people with a registered device; now the notification is
                // stored server-side and the app's feed shows it regardless,
                // so a missing device no longer means unreachable.
                userIds = await _db.Users.Select(u => u.Id).ToListAsync(ct);
                audienceLabel = "everyone";
                break;
        }

        if (userIds.Count == 0)
        {
            return new SendNotificationResponse
            {
                Recipients = 0,
                Devices = 0,
                Delivered = false,
                Message = "Nobody matches that audience."
            };
        }

        var data = new Dictionary<string, string> { ["type"] = "account" };
        if (!string.IsNullOrWhiteSpace(req.Route)) data["route"] = req.Route.Trim();

        var devices = await _push.SendToUsersAsync(
            userIds, new PushMessage(title, body, data, req.ImageUrl), ct);

        // Attributed so a broadcast can always be traced back to a person.
        _log.LogInformation(
            "{Staff} sent \"{Title}\" to {Audience}: {Devices} device(s).",
            me!.Email, title, audienceLabel, devices);

        return new SendNotificationResponse
        {
            Recipients = userIds.Count,
            Devices = devices,
            Delivered = _push.IsConfigured,
            Message = _push.IsConfigured
                ? $"Saved to {userIds.Count} feed(s); pushed to {devices} device(s)."
                : $"Saved to {userIds.Count} feed(s) — everyone sees it in the app. "
                  + "Push skipped (no service account configured)."
        };
    }

    private static bool TryParseRole(string? value, out UserRole role)
    {
        role = default;
        return !string.IsNullOrWhiteSpace(value)
               && Enum.TryParse(value, ignoreCase: true, out role);
    }
}
