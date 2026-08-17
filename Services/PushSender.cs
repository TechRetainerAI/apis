using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using MeDan.Api.Data;
using MeDan.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MeDan.Api.Services;

/// <summary>One notification, addressed to a user rather than a device.</summary>
public record PushMessage(string Title, string Body, IDictionary<string, string>? Data = null, string? ImageUrl = null);

/// <summary>
/// Sends push notifications through FCM's HTTP v1 API.
///
/// v1 rather than the legacy endpoint: the legacy server key is deprecated and
/// being switched off. v1 authenticates with a short-lived OAuth token minted
/// from the Firebase service account, which the Google.Apis.Auth library
/// caches and refreshes.
///
/// Sending is best-effort by design. A booking must not fail because a phone
/// could not be reached, so every path here swallows its errors after logging
/// them. The one thing it does act on is a token FCM reports as permanently
/// dead, which gets disabled so it is not retried forever.
/// </summary>
public class PushSender
{
    private const string Scope = "https://www.googleapis.com/auth/firebase.messaging";

    private readonly AppDbContext _db;
    private readonly HttpClient _http;
    private readonly PushOptions _opt;
    private readonly ILogger<PushSender> _log;

    private GoogleCredential? _credential;

    public PushSender(
        AppDbContext db,
        HttpClient http,
        IOptions<PushOptions> opt,
        ILogger<PushSender> log)
    {
        _db = db;
        _http = http;
        _opt = opt.Value;
        _log = log;
    }

    /// <summary>False when no service account is configured — nothing is sent.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_opt.ServiceAccountPath) ||
        !string.IsNullOrWhiteSpace(_opt.ServiceAccountJson);

    /// <summary>
    /// Delivers to every live device belonging to any of <paramref name="userIds"/>.
    ///
    /// Returns how many devices were addressed, which is what a staff member
    /// sending a broadcast actually wants to know — a user with no app
    /// installed contributes nothing.
    /// </summary>
    public async Task<int> SendToUsersAsync(
        IReadOnlyCollection<Guid> userIds, PushMessage message, CancellationToken ct = default)
    {
        if (userIds.Count == 0) return 0;

        await PersistAsync(userIds, message, ct);

        var tokens = await _db.DeviceTokens
            .Where(d => userIds.Contains(d.UserId) && d.DisabledAt == null)
            .Select(d => d.Token)
            .ToListAsync(ct);

        if (tokens.Count == 0) return 0;

        if (!IsConfigured)
        {
            _log.LogInformation(
                "Push not configured — would have sent \"{Title}\" to {Count} device(s).",
                message.Title, tokens.Count);
            return tokens.Count;
        }

        string accessToken;
        try
        {
            accessToken = await GetAccessTokenAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Could not mint an FCM access token — broadcast skipped.");
            return 0;
        }

        foreach (var token in tokens)
        {
            if (ct.IsCancellationRequested) break;
            await SendOneAsync(token, message, accessToken, ct);
        }

        return tokens.Count;
    }

    /// <summary>Delivers to every live device the user has registered.</summary>
    public async Task SendToUserAsync(Guid userId, PushMessage message, CancellationToken ct = default)
    {
        await PersistAsync(new[] { userId }, message, ct);
        var tokens = await _db.DeviceTokens
            .Where(d => d.UserId == userId && d.DisabledAt == null)
            .Select(d => d.Token)
            .ToListAsync(ct);

        if (tokens.Count == 0)
        {
            _log.LogDebug("No push tokens for user {User}.", userId);
            return;
        }

        if (!IsConfigured)
        {
            _log.LogInformation(
                "Push not configured — would have sent \"{Title}\" to {Count} device(s) for {User}.",
                message.Title, tokens.Count, userId);
            return;
        }

        string accessToken;
        try
        {
            accessToken = await GetAccessTokenAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Could not mint an FCM access token — push skipped.");
            return;
        }

        foreach (var token in tokens)
        {
            await SendOneAsync(token, message, accessToken, ct);
        }
    }

    private async Task SendOneAsync(
        string token, PushMessage message, string accessToken, CancellationToken ct)
    {
        // `data` values must be strings in v1 — anything else is rejected.
        var data = new Dictionary<string, string>();
        if (message.Data is not null)
            foreach (var (k, v) in message.Data) data[k] = v;

        var payload = new
        {
            message = new
            {
                token,
                notification = new { title = message.Title, body = message.Body },
                data,
                android = new
                {
                    priority = "high",
                    // Android renders the image natively in the expanded push.
                    notification = new
                    {
                        channel_id = "medan_default",
                        image = message.ImageUrl
                    }
                },
                apns = new
                {
                    payload = new { aps = new { sound = "default", badge = 1 } },
                    // iOS needs a notification service extension to actually
                    // attach this; harmless to send until the app has one.
                    fcm_options = new { image = message.ImageUrl }
                }
            }
        };

        var url = $"https://fcm.googleapis.com/v1/projects/{_opt.ProjectId}/messages:send";
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            using var res = await _http.SendAsync(req, ct);
            if (res.IsSuccessStatusCode) return;

            var body = await res.Content.ReadAsStringAsync(ct);

            // 404 UNREGISTERED / 400 INVALID_ARGUMENT mean this token will never
            // work again — the app was uninstalled or FCM rotated it.
            if (res.StatusCode is System.Net.HttpStatusCode.NotFound
                or System.Net.HttpStatusCode.BadRequest)
            {
                await DisableTokenAsync(token, ct);
                _log.LogInformation("Disabled dead push token ({Status}).", res.StatusCode);
                return;
            }

            _log.LogWarning("FCM send failed: {Status} {Body}", res.StatusCode, body);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "FCM send threw — notification dropped.");
        }
    }

    /// <summary>
    /// Writes the notification into MeDan's own table before any push attempt.
    ///
    /// This is the delivery that depends on nobody: the app's bell reads it
    /// whether or not FCM is configured, reachable, or permitted. Push, when
    /// it works, is just the faster knock on the door.
    /// </summary>
    private async Task PersistAsync(
        IEnumerable<Guid> userIds, PushMessage message, CancellationToken ct)
    {
        try
        {
            var type = message.Data is not null &&
                       message.Data.TryGetValue("type", out var t) ? t : "account";
            string? route = message.Data is not null &&
                            message.Data.TryGetValue("route", out var r) ? r : null;

            foreach (var id in userIds)
            {
                _db.Notifications.Add(new Models.UserNotification
                {
                    UserId = id,
                    Title = message.Title,
                    Body = message.Body,
                    Type = type,
                    Route = route,
                    ImageUrl = message.ImageUrl
                });
            }
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Never let the feed row block the flow that triggered it.
            _log.LogWarning(ex, "Could not persist notification \"{Title}\".", message.Title);
        }
    }

    private async Task DisableTokenAsync(string token, CancellationToken ct)
    {
        var row = await _db.DeviceTokens.FirstOrDefaultAsync(d => d.Token == token, ct);
        if (row is null) return;
        row.DisabledAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        _credential ??= (string.IsNullOrWhiteSpace(_opt.ServiceAccountJson)
                ? GoogleCredential.FromFile(_opt.ServiceAccountPath!)
                : GoogleCredential.FromJson(_opt.ServiceAccountJson))
            .CreateScoped(Scope);

        // The underlying token is cached and auto-refreshed by the library.
        return await _credential.UnderlyingCredential.GetAccessTokenForRequestAsync(
            cancellationToken: ct);
    }
}
