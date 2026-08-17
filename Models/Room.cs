using System.ComponentModel.DataAnnotations;

namespace MeDan.Api.Models;

/// <summary>
/// A room within a hostel. Students book a single bed/space in a room
/// (see <see cref="Bed"/>), so <see cref="Capacity"/> is how many can share it.
/// </summary>
public class Room
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid HostelId { get; set; }
    public Hostel Hostel { get; set; } = default!;

    [MaxLength(80)]
    public string Label { get; set; } = default!;   // e.g. "Room 101"

    public RoomType RoomType { get; set; } = RoomType.Single;

    /// <summary>Number of beds/spaces in the room (1, 2, 3, or 4).</summary>
    public int Capacity { get; set; } = 1;

    /// <summary>Denormalized count of beds still available (kept in sync when beds change).</summary>
    public int AvailableBeds { get; set; }

    /// <summary>Price per bed/space per semester, GH₵.</summary>
    public int PricePerBedPerSemester { get; set; }

    public Gender Gender { get; set; } = Gender.Mixed;

    [MaxLength(30)]
    public string? Floor { get; set; }

    public RoomStatus Status { get; set; } = RoomStatus.Available;

    // Navigation
    public ICollection<Bed> Beds { get; set; } = new List<Bed>();
}
