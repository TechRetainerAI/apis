namespace MeDan.Api.Models;

/// <summary>
/// A notification stored server-side — MeDan's own delivery channel.
///
/// Push (FCM) can only reach a phone while Apple/Google cooperate; this table
/// depends on nobody. Every notification is written here first, so the app's
/// bell shows the same history whether or not a push ever arrived, and it
/// survives reinstalls because the server, not the handset, owns it.
/// </summary>
public class UserNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    public string Title { get; set; } = default!;
    public string Body { get; set; } = default!;

    /// <summary>App icon bucket: payment | booking | checkIn | account …</summary>
    public string Type { get; set; } = "account";

    /// <summary>In-app route to open on tap, when there is one.</summary>
    public string? Route { get; set; }

    /// <summary>Poster/attachment shown in the feed and in rich pushes.</summary>
    public string? ImageUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
}
