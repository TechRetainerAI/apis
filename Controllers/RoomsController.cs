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
            PricePerBedPerSemester = req.PricePerSemester,
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
        Status = r.Status.ToCamel(),
        Capacity = r.Capacity,
        AvailableBeds = r.AvailableBeds,
        Gender = r.Gender.ToCamel()
    };
}
