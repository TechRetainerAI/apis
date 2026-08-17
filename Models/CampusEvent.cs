namespace MeDan.Api.Models;

/// <summary>
/// A campus or hostel event shown on the app's Events tab.
///
/// Authored by platform staff from the admin dashboard. Deliberately simple —
/// no tickets, no RSVPs — because the tab's job is telling students what is
/// happening near them, not selling entry.
/// </summary>
public class CampusEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = default!;
    public string? Description { get; set; }

    /// <summary>Where it happens, in words ("UENR Auditorium").</summary>
    public string Venue { get; set; } = default!;

    /// <summary>Null shows the event on every campus.</summary>
    public string? CampusCode { get; set; }
    public Campus? Campus { get; set; }

    public DateTime StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }

    public string? ImageUrl { get; set; }

    public Guid PostedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
