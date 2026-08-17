using MeDan.Api.Auth;
using MeDan.Api.Data;
using MeDan.Api.Dtos;
using MeDan.Api.Helpers;
using MeDan.Api.Models;
using MeDan.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MeDan.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HostelsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly CurrentUser _current;
    private readonly IImageStorage _images;

    public HostelsController(AppDbContext db, CurrentUser current, IImageStorage images)
    {
        _db = db;
        _current = current;
        _images = images;
    }

    /// <summary>Public listing with simple filters: campus, type, price range, verified, search.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<HostelSummary>>> List(
        [FromQuery] string? campus,
        [FromQuery] PropertyType? type,
        [FromQuery] RoomType? roomType,
        [FromQuery] int? maxPrice,
        [FromQuery] bool? verified,
        [FromQuery] string? q,
        CancellationToken ct = default)
    {
        var query = _db.Hostels.AsNoTracking()
            .Include(h => h.Campus)
            .Include(h => h.Company)
            .Include(h => h.Photos)
            .Include(h => h.Amenities).ThenInclude(a => a.Amenity)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(campus)) query = query.Where(h => h.CampusCode == campus);
        if (type is not null) query = query.Where(h => h.PropertyType == type);

        // "1 in a room", "2 in a room" … — a hostel qualifies if it offers at
        // least one room of that layout, not if every room matches.
        if (roomType is not null)
            query = query.Where(h => h.Rooms.Any(r => r.RoomType == roomType));

        if (maxPrice is not null) query = query.Where(h => h.MinPrice <= maxPrice);
        if (verified is true) query = query.Where(h => h.IsVerified);
        // Students search for places, not just names — "Sunyani" is a city, and
        // matching only the hostel name meant that returned nothing even though
        // every UENR hostel is in Sunyani.
        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(h =>
                h.Name.Contains(q) ||
                h.Address.Contains(q) ||
                h.CampusCode.Contains(q) ||
                h.Campus.City.Contains(q) ||
                h.Campus.FullName.Contains(q) ||
                h.Company!.Name.Contains(q));
        }

        var items = await query
            .OrderByDescending(h => h.IsVerified).ThenBy(h => h.DistanceKm)
            .Select(h => ToSummary(h))
            .ToListAsync(ct);

        return items;
    }

    /// <summary>
    /// Listings belonging to the caller — every company they own or work for.
    ///
    /// Scoped on the server rather than by filtering the public list in the
    /// app: that approach downloaded every hostel on the platform and matched
    /// only the company owner, so a worker saw nothing at all.
    /// </summary>
    [HttpGet("mine")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<HostelSummary>>> Mine(CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return Unauthorized("Register first.");

        var companyIds = await _db.Companies
            .Where(c => c.OwnerUserId == me.Id || c.Members.Any(m => m.UserId == me.Id))
            .Select(c => c.Id)
            .ToListAsync(ct);

        if (companyIds.Count == 0) return new List<HostelSummary>();

        var items = await _db.Hostels.AsNoTracking()
            .Include(h => h.Campus)
            .Include(h => h.Company)
            .Include(h => h.Photos)
            .Include(h => h.Amenities).ThenInclude(a => a.Amenity)
            .Where(h => companyIds.Contains(h.CompanyId))
            .OrderBy(h => h.Name)
            .Select(h => ToSummary(h))
            .ToListAsync(ct);

        return items;
    }

    /// <summary>Full detail including rooms and amenities.</summary>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<HostelDetail>> Get(Guid id, CancellationToken ct)
    {
        var detail = await BuildDetailAsync(id, ct);
        return detail is null ? NotFound() : detail;
    }

    private async Task<HostelDetail?> BuildDetailAsync(Guid id, CancellationToken ct)
    {
        var h = await _db.Hostels.AsNoTracking()
            .Include(x => x.Company)
            .Include(x => x.Photos)
            .Include(x => x.Amenities).ThenInclude(a => a.Amenity)
            .Include(x => x.Rooms)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (h is null) return null;

        return new HostelDetail
        {
            Id = h.Id,
            Name = h.Name,
            Campus = h.CampusCode,
            OwnerId = h.Company.OwnerUserId,
            Address = h.Address,
            Lat = h.Latitude,
            Lng = h.Longitude,
            DistanceKm = h.DistanceKm,
            MinPrice = h.MinPrice,
            MaxPrice = h.MaxPrice,
            Photos = h.Photos.OrderBy(p => p.SortOrder).Select(p => p.Url).ToList(),
            Amenities = h.Amenities.Select(a => a.Amenity.IconKey ?? a.Amenity.Name).ToList(),
            IsVerified = h.IsVerified,
            Rating = h.Rating,
            ReviewCount = h.ReviewCount,
            Description = h.Description,
            // A listing without its own number falls back to the company's —
            // "contact details after booking" should mean we have none at all,
            // not that a field was left blank when the listing was typed in.
            ContactPhone = string.IsNullOrWhiteSpace(h.ContactPhone)
                ? h.Company.Phone
                : h.ContactPhone,
            PropertyType = h.PropertyType.ToCamel(),
            CompanyId = h.CompanyId,
            Rooms = h.Rooms.Select(ToRoomSummary).ToList(),
            PhotoItems = h.Photos.OrderBy(p => p.SortOrder)
                .Select(p => new PhotoResponse
                {
                    Id = p.Id, Url = p.Url, IsCover = p.IsCover, SortOrder = p.SortOrder
                })
                .ToList()
        };
    }

    /// <summary>
    /// Create a listing. Caller must be the owner or a worker (with posting rights)
    /// of the target company. Enforces the Starter-tier listing limit.
    /// </summary>
    [HttpPost]
    [Authorize]
    public async Task<ActionResult<HostelDetail>> Create(CreateHostelRequest req, CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return Unauthorized("Register first.");

        // Resolve the target company: explicit id, else the caller's, else auto-create one.
        Company company;
        if (req.CompanyId is Guid cid)
        {
            var found = await _db.Companies.Include(c => c.Members).FirstOrDefaultAsync(c => c.Id == cid, ct);
            if (found is null) return NotFound("Company not found.");
            var isOwner = found.OwnerUserId == me.Id;
            var asWorker = found.Members.FirstOrDefault(m => m.UserId == me.Id);
            if (!isOwner && !(asWorker is not null && asWorker.CanPostListings)) return Forbid();
            company = found;
        }
        else
        {
            company = await _db.Companies.Include(c => c.Members)
                          .FirstOrDefaultAsync(c => c.OwnerUserId == me.Id, ct)
                      ?? await CreateDefaultCompanyAsync(me, ct);
        }

        if (company.ListingLimit is int limit)
        {
            var count = await _db.Hostels.CountAsync(h => h.CompanyId == company.Id, ct);
            if (count >= limit)
                return BadRequest($"Listing limit ({limit}) reached for the {company.Tier} tier. Upgrade to add more.");
        }

        var hostel = new Hostel
        {
            CompanyId = company.Id,
            Name = req.Name,
            PropertyType = req.PropertyType,
            Description = req.Description,
            CampusCode = req.Campus,
            Address = req.Address,
            Latitude = req.Lat,
            Longitude = req.Lng,
            DistanceKm = req.DistanceKm,
            MinPrice = req.MinPrice,
            MaxPrice = req.MaxPrice,
            ContactPhone = req.ContactPhone,
            PostedByUserId = me.Id
        };

        foreach (var (url, i) in req.PhotoUrls.Select((u, i) => (u, i)))
            hostel.Photos.Add(new HostelPhoto { Url = url, IsCover = i == 0, SortOrder = i });

        if (req.Amenities.Count > 0)
        {
            var keys = req.Amenities.Select(k => k.ToLower()).ToList();
            var matched = await _db.Amenities
                .Where(a => a.IconKey != null && keys.Contains(a.IconKey.ToLower()))
                .ToListAsync(ct);
            foreach (var a in matched)
                hostel.Amenities.Add(new HostelAmenity { AmenityId = a.Id });
        }

        _db.Hostels.Add(hostel);
        await _db.SaveChangesAsync(ct);

        var detail = await BuildDetailAsync(hostel.Id, ct);
        return CreatedAtAction(nameof(Get), new { id = hostel.Id }, detail);
    }

    /// <summary>
    /// Replace a listing's details (owner/worker, or platform staff). Photos and rooms
    /// are managed through their own routes; MinPrice/MaxPrice here are overwritten by
    /// the room price range as soon as a room is added or changed.
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize]
    public async Task<ActionResult<HostelDetail>> Update(Guid id, UpdateHostelRequest req, CancellationToken ct)
    {
        var hostel = await _db.Hostels
            .Include(h => h.Company).ThenInclude(c => c.Members)
            .Include(h => h.Amenities)
            .FirstOrDefaultAsync(h => h.Id == id, ct);
        if (hostel is null) return NotFound("Hostel not found.");
        if (!await CanManage(hostel, ct)) return Forbid();

        if (!await _db.Campuses.AnyAsync(c => c.Code == req.Campus, ct))
            return BadRequest($"Unknown campus code '{req.Campus}'.");

        hostel.Name = req.Name;
        hostel.PropertyType = req.PropertyType;
        hostel.Description = req.Description;
        hostel.CampusCode = req.Campus;
        hostel.Address = req.Address;
        hostel.Latitude = req.Lat;
        hostel.Longitude = req.Lng;
        hostel.DistanceKm = req.DistanceKm;
        hostel.MinPrice = req.MinPrice;
        hostel.MaxPrice = req.MaxPrice;
        hostel.ContactPhone = req.ContactPhone;
        hostel.UpdatedAt = DateTime.UtcNow;

        // Amenities are a full replace: drop the current set, then re-add what matched.
        _db.RemoveRange(hostel.Amenities);
        if (req.Amenities.Count > 0)
        {
            var keys = req.Amenities.Select(k => k.ToLower()).ToList();
            var matched = await _db.Amenities
                .Where(a => a.IconKey != null && keys.Contains(a.IconKey.ToLower()))
                .ToListAsync(ct);
            foreach (var a in matched)
                _db.Add(new HostelAmenity { HostelId = hostel.Id, AmenityId = a.Id });
        }

        await _db.SaveChangesAsync(ct);
        return await BuildDetailAsync(hostel.Id, ct) is { } detail ? detail : NotFound();
    }

    /// <summary>
    /// Delete a listing (owner/worker, or platform staff). Rooms, beds, photos and
    /// amenities go with it. Refused while any booking references the hostel — those
    /// are financial records, and the FK is Restrict, so this reports the conflict
    /// instead of surfacing a database error.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var hostel = await _db.Hostels
            .Include(h => h.Photos)
            .Include(h => h.Company).ThenInclude(c => c.Members)
            .FirstOrDefaultAsync(h => h.Id == id, ct);
        if (hostel is null) return NotFound("Hostel not found.");
        if (!await CanManage(hostel, ct)) return Forbid();

        var bookings = await _db.Bookings.CountAsync(b => b.HostelId == id, ct);
        if (bookings > 0)
            return Conflict(
                $"This hostel has {bookings} booking(s) and cannot be deleted. " +
                "Unverify it instead so it stops appearing to students.");

        foreach (var photo in hostel.Photos) _images.Delete(photo.Url);

        _db.Hostels.Remove(hostel);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Creates a starter company for an owner posting their first listing.</summary>
    private async Task<Company> CreateDefaultCompanyAsync(AppUser me, CancellationToken ct)
    {
        var company = new Company { Name = $"{me.FullName}'s Hostels", OwnerUserId = me.Id };
        company.Apply(CompanyTier.Starter);
        company.Members.Add(new CompanyMember { UserId = me.Id, Role = CompanyRole.Owner, CanPostListings = true });
        if (me.Role == UserRole.Student) me.Role = UserRole.Owner;
        _db.Companies.Add(company);
        await _db.SaveChangesAsync(ct);
        return company;
    }

    /// <summary>
    /// Upload one or more images for a hostel (owner/worker). multipart/form-data, field name "files".
    /// The first photo of a hostel becomes its cover automatically.
    /// </summary>
    [HttpPost("{id:guid}/photos")]
    [Authorize]
    public async Task<ActionResult<List<PhotoResponse>>> AddPhotos(Guid id, [FromForm] List<IFormFile> files, CancellationToken ct)
    {
        if (files is null || files.Count == 0) return BadRequest("No files uploaded.");

        var hostel = await _db.Hostels
            .Include(h => h.Photos)
            .Include(h => h.Company).ThenInclude(c => c.Members)
            .FirstOrDefaultAsync(h => h.Id == id, ct);
        if (hostel is null) return NotFound("Hostel not found.");
        if (!await CanManage(hostel, ct)) return Forbid();

        var nextOrder = hostel.Photos.Count == 0 ? 0 : hostel.Photos.Max(p => p.SortOrder) + 1;
        var hasCover = hostel.Photos.Any(p => p.IsCover);
        var added = new List<HostelPhoto>();

        foreach (var file in files)
        {
            string url;
            try { url = await _images.SaveAsync(file, "hostels", ct); }
            catch (InvalidImageException ex) { return BadRequest(ex.Message); }

            var photo = new HostelPhoto
            {
                HostelId = hostel.Id,
                Url = url,
                SortOrder = nextOrder++,
                IsCover = !hasCover && added.Count == 0
            };
            // Add through the DbSet, not hostel.Photos: the parent is tracked, and a
            // child whose key is already set (Guid.NewGuid() default) gets discovered
            // as Modified — issuing an UPDATE against a row that doesn't exist yet.
            _db.HostelPhotos.Add(photo);
            added.Add(photo);
        }

        hostel.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return added.Select(p => new PhotoResponse
        {
            Id = p.Id, Url = p.Url, IsCover = p.IsCover, SortOrder = p.SortOrder
        }).ToList();
    }

    /// <summary>Delete a hostel photo (owner/worker). Promotes another photo to cover if needed.</summary>
    [HttpDelete("{id:guid}/photos/{photoId:guid}")]
    [Authorize]
    public async Task<IActionResult> DeletePhoto(Guid id, Guid photoId, CancellationToken ct)
    {
        var hostel = await _db.Hostels
            .Include(h => h.Photos)
            .Include(h => h.Company).ThenInclude(c => c.Members)
            .FirstOrDefaultAsync(h => h.Id == id, ct);
        if (hostel is null) return NotFound("Hostel not found.");
        if (!await CanManage(hostel, ct)) return Forbid();

        var photo = hostel.Photos.FirstOrDefault(p => p.Id == photoId);
        if (photo is null) return NotFound("Photo not found.");

        _images.Delete(photo.Url);
        hostel.Photos.Remove(photo);
        _db.HostelPhotos.Remove(photo);

        // Keep a cover if we removed the cover and others remain.
        if (photo.IsCover && hostel.Photos.Count > 0)
            hostel.Photos.OrderBy(p => p.SortOrder).First().IsCover = true;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<bool> CanManage(Hostel hostel, CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return false;
        // Platform staff administer every listing from the admin dashboard, not just
        // the ones belonging to a company they're a member of.
        if (me.Role is UserRole.Admin or UserRole.Manager) return true;
        if (hostel.Company.OwnerUserId == me.Id) return true;
        var worker = hostel.Company.Members.FirstOrDefault(m => m.UserId == me.Id);
        return worker is not null && worker.CanPostListings;
    }

    private static HostelSummary ToSummary(Hostel h) => new()
    {
        Id = h.Id,
        Name = h.Name,
        Campus = h.CampusCode,
        OwnerId = h.Company.OwnerUserId,
        Address = h.Address,
        Lat = h.Latitude,
        Lng = h.Longitude,
        DistanceKm = h.DistanceKm,
        MinPrice = h.MinPrice,
        MaxPrice = h.MaxPrice,
        Photos = h.Photos.OrderBy(p => p.SortOrder).Select(p => p.Url).ToList(),
        Amenities = h.Amenities.Select(a => a.Amenity.IconKey ?? a.Amenity.Name).ToList(),
        IsVerified = h.IsVerified,
        Rating = h.Rating,
        ReviewCount = h.ReviewCount,
        Description = h.Description,
        ContactPhone = h.ContactPhone,
        PropertyType = h.PropertyType.ToCamel(),
        CompanyId = h.CompanyId
    };

    private static RoomSummary ToRoomSummary(Room r) => new()
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
