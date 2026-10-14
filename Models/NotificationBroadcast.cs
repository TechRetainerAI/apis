using System.ComponentModel.DataAnnotations;

namespace MeDan.Api.Models;

/// <summary>
/// One staff send from the dashboard, recorded once however many feeds it
/// reached. <see cref="UserNotification"/> rows are the per-recipient copies
/// the app shows; this is the dashboard's own answer to "what did we send,
/// to whom, and did it go out?" — which used to live only in the logs.
/// </summary>
public class NotificationBroadcast
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(80)] public string Title { get; set; } = default!;
    [MaxLength(500)] public string Body { get; set; } = default!;

    [MaxLength(200)] public string? Route { get; set; }
    [MaxLength(500)] public string? ImageUrl { get; set; }

    /// <summary>What was targeted, as the sender saw it: "everyone", "role student", "user {id}".</summary>
    [MaxLength(80)] public string Audience { get; set; } = default!;

    /// <summary>Feeds written — the real recipient count.</summary>
    public int Recipients { get; set; }

    /// <summary>Devices the push went to (0 when push isn't configured).</summary>
    public int Devices { get; set; }

    /// <summary>False when FCM wasn't configured and only the in-app feeds got it.</summary>
    public bool Delivered { get; set; }

    public Guid? SentByUserId { get; set; }
    [MaxLength(150)] public string SentByName { get; set; } = default!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
