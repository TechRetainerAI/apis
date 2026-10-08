using MeDan.Api.Auth;
using MeDan.Api.Data;
using MeDan.Api.Dtos;
using MeDan.Api.Helpers;
using MeDan.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MeDan.Api.Controllers;

[ApiController]
[Route("api/hostels/{hostelId:guid}/rooms")]
public class RoomsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly CurrentUser _current;

    public RoomsController(AppDbContext db, CurrentUser current)
    {
        _db = db;
        _current = current;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<RoomSummary>>> List(Guid hostelId, CancellationToken ct)
    {
        var rooms = await _db.Rooms.AsNoTracking()
            .Where(r => r.HostelId == hostelId)
            .OrderBy(r => r.Label)
            .Select(r => ToSummary(r))
            .ToListAsync(ct);
        return rooms;
    }

    /// <summary>
    /// Add a room to a hostel. Auto-creates <c>Capacity</c> beds (Bed A, Bed B, …),
    /// then refreshes the hostel's denormalized price range. Owner/worker only.
    /// </summary>
    [HttpPost]
    [Authorize]
    public async Task<ActionResult<RoomSummary>> Create(Guid hostelId, CreateRoomRequest req, CancellationToken ct)
    {
        var hostel = await _db.Hostels
            .Include(h => h.Company).ThenInclude(c => c.Members)
            .FirstOrDefaultAsync(h => h.Id == hostelId, ct);
        if (hostel is null) return NotFound("Hostel not found.");

        if (!await CanManage(hostel, ct)) return Forbid();

        var room = new Room
        {
            HostelId = hostelId,
            Label = req.Label,
            RoomType = req.Type,
            Capacity = req.Capacity,
            AvailableBeds = req.Capacity,
            // The owner types their asking price; students see it plus MeDan's 5%.
            PricePerBedPerSemester = Pricing.WithMarkup(req.PricePerSemester),
            Gender = req.Gender,
            Floor = req.Floor,
            Status = RoomStatus.Available
        };

        for (var i = 0; i < req.Capacity; i++)
            room.Beds.Add(new Bed { Label = $"Bed {(char)('A' + i)}", Status = BedStatus.Available });

        _db.Rooms.Add(room);
        await _db.SaveChangesAsync(ct);

        await RefreshHostelPriceRange(hostelId, ct);

        return CreatedAtAction(nameof(List), new { hostelId }, ToSummary(room));
    }

    /// <summary>
    /// Edit a room after it is listed — the manage screen's save. Owner/worker, or
    /// platform staff.
    ///
    /// The price means what it means on create: the owner's asking price, with
    /// MeDan's 5% added on top before storing. That is safe because the edit form
    /// loads <c>ownerPrice</c>, not the listed figure — the number round-trips
    /// unchanged, so re-saving a room never walks its price upward.
    /// </summary>
    [HttpPut("{roomId:guid}")]
    [Authorize]
    public async Task<ActionResult<RoomSummary>> Update(
        Guid hostelId, Guid roomId, UpdateRoomRequest req, CancellationToken ct)
    {
        var hostel = await _db.Hostels
            .Include(h => h.Company).ThenInclude(c => c.Members)
            .FirstOrDefaultAsync(h => h.Id == hostelId, ct);
        if (hostel is null) return NotFound("Hostel not found.");
        if (!await CanManage(hostel, ct)) return Forbid();

        var room = await _db.Rooms
            .Include(r => r.Beds)
            .FirstOrDefaultAsync(r => r.Id == roomId && r.HostelId == hostelId, ct);
        if (room is null) return NotFound("Room not found.");

        // Reconcile beds against a local list rather than room.Beds. EF's relationship
        // fixup puts a newly added bed into that navigation by itself, so adding to it
        // here as well counts every new bed twice — and the inflated number is what
        // AvailableBeds would be saved as.
        var beds = room.Beds.ToList();

        // Shrinking can only give up beds nobody holds. Students already in the room
        // keep theirs, and the save is refused rather than quietly leaving the room
        // over capacity.
        if (req.Capacity < beds.Count)
        {
            var free = beds.Count(b => b.Status == BedStatus.Available);
            var surplus = beds.Count - req.Capacity;
            if (free < surplus)
                return Conflict(
                    $"Can't reduce to {req.Capacity} bed(s): only {free} of " +
                    $"{beds.Count} are free. Beds that are booked or occupied " +
                    "have to be released first.");

            foreach (var bed in beds
                         .Where(b => b.Status == BedStatus.Available)
                         .OrderByDescending(b => b.Label)
                         .Take(surplus)
                         .ToList())
            {
                beds.Remove(bed);
                room.Beds.Remove(bed);
                _db.Remove(bed);
            }
        }
        else if (req.Capacity > beds.Count)
        {
            // Continue the lettering rather than restarting it, so labels stay unique.
            for (var i = beds.Count; i < req.Capacity; i++)
            {
                var bed = new Bed
                {
                    RoomId = room.Id,
                    Label = $"Bed {(char)('A' + i)}",
                    Status = BedStatus.Available
                };

                // Added explicitly. Unlike Create, the room here is already tracked, and
                // Bed initialises its own Id — EF reads that as an existing row and
                // issues an UPDATE that matches nothing ("expected to affect 1 row(s),
                // but actually affected 0") instead of inserting the bed.
                _db.Beds.Add(bed);
                beds.Add(bed);
            }
        }

        room.Label = req.Label;
        room.RoomType = req.Type;
        room.Capacity = req.Capacity;
        room.Gender = req.Gender;
        // Same rule as create: the owner types what they want to receive.
        room.PricePerBedPerSemester = Pricing.WithMarkup(req.PricePerSemester);
        if (req.Floor is not null) room.Floor = req.Floor;

        // Kept in step with the bed rows, which are the real source of availability.
        room.AvailableBeds = room.Beds.Count(b => b.Status == BedStatus.Available);

        await _db.SaveChangesAsync(ct);

        // A price or capacity change moves the hostel's advertised range.
        await RefreshHostelPriceRange(hostelId, ct);

        return ToSummary(room);
    }

    /// <summary>
    /// Remove a room that was never booked. Any booking — past or active — pins
    /// the room (FK Restrict; students' booking history must keep pointing at a
    /// real room), so those return 409: maintenance status is how a once-booked
    /// room leaves the market. Beds cascade with the room.
    /// </summary>
    [HttpDelete("{roomId:guid}")]
    [Authorize]
    public async Task<IActionResult> Delete(Guid hostelId, Guid roomId, CancellationToken ct)
    {
        var hostel = await _db.Hostels
            .Include(h => h.Company).ThenInclude(c => c.Members)
            .FirstOrDefaultAsync(h => h.Id == hostelId, ct);
        if (hostel is null) return NotFound("Hostel not found.");
        if (!await CanManage(hostel, ct)) return Forbid();

        var room = await _db.Rooms
            .FirstOrDefaultAsync(r => r.Id == roomId && r.HostelId == hostelId, ct);
        if (room is null) return NotFound("Room not found.");

        if (await _db.Bookings.AnyAsync(b => b.RoomId == roomId, ct))
            return Conflict(
                "This room has bookings (past or active) and can't be deleted. " +
                "Set it to maintenance to take it off the market instead.");

        _db.Rooms.Remove(room);
        await _db.SaveChangesAsync(ct);

        await RefreshHostelPriceRange(hostelId, ct);
        return NoContent();
    }

    /// <summary>
    /// Take a room off the market or put it back (owner/worker, or platform staff).
    /// Bed availability is untouched — this is the room-level switch the manager
    /// dashboard uses to flag maintenance.
    /// </summary>
    [HttpPut("{roomId:guid}/status")]
    [Authorize]
    public async Task<ActionResult<RoomSummary>> SetStatus(
        Guid hostelId, Guid roomId, SetRoomStatusRequest req, CancellationToken ct)
    {
        var hostel = await _db.Hostels
            .Include(h => h.Company).ThenInclude(c => c.Members)
            .FirstOrDefaultAsync(h => h.Id == hostelId, ct);
        if (hostel is null) return NotFound("Hostel not found.");
        if (!await CanManage(hostel, ct)) return Forbid();

        var room = await _db.Rooms
            .FirstOrDefaultAsync(r => r.Id == roomId && r.HostelId == hostelId, ct);
        if (room is null) return NotFound("Room not found.");

        room.Status = req.Status;
        await _db.SaveChangesAsync(ct);
        return ToSummary(room);
    }

    private async Task<bool> CanManage(Hostel hostel, CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return false;
        // Platform staff administer every listing from the admin dashboard.
        if (me.Role is UserRole.Admin or UserRole.Manager) return true;
        if (hostel.Company.OwnerUserId == me.Id) return true;
        var worker = hostel.Company.Members.FirstOrDefault(m => m.UserId == me.Id);
        return worker is not null && worker.CanPostListings;
    }

    private async Task RefreshHostelPriceRange(Guid hostelId, CancellationToken ct)
    {
        var prices = await _db.Rooms.Where(r => r.HostelId == hostelId)
            .Select(r => r.PricePerBedPerSemester).ToListAsync(ct);
        var hostel = await _db.Hostels.FirstAsync(h => h.Id == hostelId, ct);
        hostel.MinPrice = prices.Count == 0 ? 0 : prices.Min();
        hostel.MaxPrice = prices.Count == 0 ? 0 : prices.Max();
        hostel.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private static RoomSummary ToSummary(Room r) => new()
    {
        Id = r.Id,
        HostelId = r.HostelId,
        Label = r.Label,
        Type = r.RoomType.ToCamel(),
        PricePerSemester = r.PricePerBedPerSemester,
        OwnerPrice = Pricing.OwnerPrice(r.PricePerBedPerSemester),
        Status = r.Status.ToCamel(),
        Capacity = r.Capacity,
        AvailableBeds = r.AvailableBeds,
        Gender = r.Gender.ToCamel()
    };
}
