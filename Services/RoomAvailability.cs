using MeDan.Api.Data;
using MeDan.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MeDan.Api.Services;

/// <summary>
/// The one place that decides what a room advertises and how a bed goes back on
/// the market.
///
/// Availability is counted from the bed rows every time, never nudged up and down
/// beside each change. The counter version drifted: releasing a bed added one with
/// no ceiling, so a booking released down two paths pushed the count past the
/// room's own capacity, and the room advertised beds it did not have.
///
/// It lives here rather than in a controller because the expiry sweeper releases
/// beds too, and two copies of this rule would be the same bug again.
/// </summary>
public static class RoomAvailability
{
    /// <summary>
    /// Recomputes a room's free-bed count and status from its beds.
    ///
    /// The caller must already have the beds loaded and mutated: the count runs
    /// over tracked entities, so it sees changes this request has not saved yet.
    /// </summary>
    public static void Sync(Room room)
    {
        room.AvailableBeds = room.Beds.Count(b => b.Status == BedStatus.Available);

        // Maintenance is the owner saying the room is off the market whatever its
        // beds say, so availability never overrides it.
        if (room.Status != RoomStatus.Maintenance)
            room.Status = room.AvailableBeds == 0 ? RoomStatus.Occupied : RoomStatus.Available;
    }

    /// <summary>
    /// Puts a booking's bed back on the market and recounts the room. Does not save —
    /// the caller decides what else belongs in the same transaction.
    /// </summary>
    public static async Task ReleaseBedAsync(AppDbContext db, Booking booking, CancellationToken ct)
    {
        var room = await db.Rooms.Include(r => r.Beds)
            .FirstAsync(r => r.Id == booking.RoomId, ct);

        var bed = room.Beds.First(b => b.Id == booking.BedId);
        bed.Status = BedStatus.Available;
        bed.CurrentBookingId = null;

        Sync(room);
    }
}
