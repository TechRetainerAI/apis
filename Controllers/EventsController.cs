using MeDan.Api.Auth;
using MeDan.Api.Data;
using MeDan.Api.Dtos;
using MeDan.Api.Models;
using MeDan.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MeDan.Api.Controllers;

/// <summary>
/// Campus events. Reading is public — the app's Events tab works signed out —
/// while writing is Admin/Manager only, from the dashboard.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class EventsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly CurrentUser _current;
    private readonly IImageStorage _images;
    private readonly ILogger<EventsController> _log;

    public EventsController(
        AppDbContext db,
        CurrentUser current,
        IImageStorage images,
        ILogger<EventsController> log)
    {
        _db = db;
        _current = current;
        _images = images;
        _log = log;
    }

    /// <summary>
    /// Upcoming events, soonest first. <paramref name="campus"/> narrows to one
    /// campus plus the events posted for everyone; <paramref name="all"/> adds
    /// the past ones (the dashboard wants those, the app does not).
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<EventResponse>>> List(
        [FromQuery] string? campus,
        [FromQuery] bool all = false,
        CancellationToken ct = default)
    {
        var q = _db.Events.AsNoTracking().AsQueryable();

        if (!all)
        {
            // "Upcoming" includes anything still running right now.
            var now = DateTime.UtcNow;
            q = q.Where(e => e.StartsAt >= now || (e.EndsAt != null && e.EndsAt >= now));
        }
        if (!string.IsNullOrWhiteSpace(campus))
            q = q.Where(e => e.CampusCode == null || e.CampusCode == campus);

        var items = await q.OrderBy(e => e.StartsAt).Take(100).ToListAsync(ct);
        return items.Select(ToResponse).ToList();
    }

    /// <summary>Create an event. Admin/Manager only.</summary>
    [HttpPost]
    [Authorize]
    public async Task<ActionResult<EventResponse>> Create(SaveEventRequest req, CancellationToken ct)
    {
        var (me, error) = await RequireStaffAsync(ct);
        if (error is not null) return error;

        if (req.Campus is not null &&
            !await _db.Campuses.AnyAsync(c => c.Code == req.Campus, ct))
            return BadRequest($"Unknown campus '{req.Campus}'.");
        if (req.EndsAt is DateTime end && end < req.StartsAt)
            return BadRequest("An event cannot end before it starts.");

        var ev = new CampusEvent
        {
            Title = req.Title.Trim(),
            Description = req.Description?.Trim(),
            Venue = req.Venue.Trim(),
            CampusCode = req.Campus,
            StartsAt = req.StartsAt,
            EndsAt = req.EndsAt,
            ImageUrl = req.ImageUrl,
            PostedByUserId = me!.Id
        };
        _db.Events.Add(ev);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("{Staff} posted event \"{Title}\".", me.Email, ev.Title);
        return CreatedAtAction(nameof(List), null, ToResponse(ev));
    }

    /// <summary>Update an event. Admin/Manager only.</summary>
    [HttpPut("{id:guid}")]
    [Authorize]
    public async Task<ActionResult<EventResponse>> Update(
        Guid id, SaveEventRequest req, CancellationToken ct)
    {
        var (_, error) = await RequireStaffAsync(ct);
        if (error is not null) return error;

        var ev = await _db.Events.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (ev is null) return NotFound();
        if (req.EndsAt is DateTime end && end < req.StartsAt)
            return BadRequest("An event cannot end before it starts.");

        ev.Title = req.Title.Trim();
        ev.Description = req.Description?.Trim();
        ev.Venue = req.Venue.Trim();
        ev.CampusCode = req.Campus;
        ev.StartsAt = req.StartsAt;
        ev.EndsAt = req.EndsAt;
        ev.ImageUrl = req.ImageUrl;
        await _db.SaveChangesAsync(ct);

        return ToResponse(ev);
    }

    /// <summary>Attach or replace the event's poster image. Admin/Manager only.</summary>
    [HttpPost("{id:guid}/image")]
    [Authorize]
    public async Task<ActionResult<EventResponse>> UploadImage(
        Guid id, IFormFile file, CancellationToken ct)
    {
        var (_, error) = await RequireStaffAsync(ct);
        if (error is not null) return error;

        var ev = await _db.Events.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (ev is null) return NotFound();

        string url;
        try { url = await _images.SaveAsync(file, "events", ct); }
        catch (InvalidImageException ex) { return BadRequest(ex.Message); }

        _images.Delete(ev.ImageUrl); // drop the previous poster, if any
        ev.ImageUrl = url;
        await _db.SaveChangesAsync(ct);
        return ToResponse(ev);
    }

    /// <summary>Delete an event. Admin/Manager only.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var (_, error) = await RequireStaffAsync(ct);
        if (error is not null) return error;

        var ev = await _db.Events.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (ev is null) return NotFound();

        _db.Events.Remove(ev);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<(AppUser? user, ActionResult? error)> RequireStaffAsync(CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return (null, Unauthorized("Register first."));
        if (me.Role is not (UserRole.Admin or UserRole.Manager)) return (null, Forbid());
        return (me, null);
    }

    private static EventResponse ToResponse(CampusEvent e) => new()
    {
        Id = e.Id,
        Title = e.Title,
        Description = e.Description,
        Venue = e.Venue,
        Campus = e.CampusCode,
        StartsAt = e.StartsAt,
        EndsAt = e.EndsAt,
        ImageUrl = e.ImageUrl,
        CreatedAt = e.CreatedAt
    };
}
