using System.ComponentModel.DataAnnotations;

namespace MeDan.Api.Models;

/// <summary>
/// A single bed/space inside a room. This is the unit a student actually books,
/// which lets multiple students share one room ("4 in a room").
/// </summary>
public class Bed
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RoomId { get; set; }
    public Room Room { get; set; } = default!;

    [MaxLength(40)]
    public string Label { get; set; } = default!;   // e.g. "Bed A"

    public BedStatus Status { get; set; } = BedStatus.Available;

    /// <summary>The booking currently holding/occupying this bed, if any.</summary>
    public Guid? CurrentBookingId { get; set; }
    public Booking? CurrentBooking { get; set; }
}
