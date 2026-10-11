using System.ComponentModel.DataAnnotations;

namespace MeDan.Api.Models;

/// <summary>
/// An image of one room, kept apart from the hostel's own gallery.
///
/// The hostel photos show the building and its shared spaces; a student choosing
/// between a two-in-a-room and a four-in-a-room wants to see those two rooms, and
/// mixing them into one gallery told them nothing about either. A room with no
/// photos of its own falls back to the hostel's on the listing.
/// </summary>
public class RoomPhoto
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RoomId { get; set; }
    public Room Room { get; set; } = default!;

    /// <summary>Site-relative, e.g. <c>/api/images/{id}</c>.</summary>
    [MaxLength(500)]
    public string Url { get; set; } = default!;

    /// <summary>The one shown on the room card. Exactly one per room once it has any.</summary>
    public bool IsCover { get; set; }

    public int SortOrder { get; set; }
}
