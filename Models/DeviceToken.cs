namespace MeDan.Api.Models;

/// <summary>Which push transport a token belongs to.</summary>
public enum DevicePlatform
{
    Android = 0,
    Ios = 1
}

/// <summary>
/// An FCM registration token — one row per app install per user.
///
/// Tokens are not stable: FCM rotates them on reinstall, restore-from-backup
/// and occasionally on its own, and the same handset can be signed into
/// different accounts over time. The token itself is therefore the primary
/// key, so re-registering an existing token simply re-points it at whoever is
/// signed in now rather than leaving the previous user subscribed.
/// </summary>
public class DeviceToken
{
    /// <summary>The FCM registration token (~150-300 chars in practice).</summary>
    public string Token { get; set; } = default!;

    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    public DevicePlatform Platform { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Bumped every time the app re-registers, so stale rows are visible.</summary>
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Set when FCM rejects the token as permanently gone (UNREGISTERED /
    /// INVALID_ARGUMENT). Kept rather than deleted so a delivery failure can be
    /// told apart from a device that never registered.
    /// </summary>
    public DateTime? DisabledAt { get; set; }

    public bool IsActive => DisabledAt is null;
}
