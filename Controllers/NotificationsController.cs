using MeDan.Api.Auth;
using MeDan.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MeDan.Api.Controllers;

public record NotificationResponse
{
    public Guid Id { get; init; }
    public string Title { get; init; } = default!;
    public string Body { get; init; } = default!;
    public string Type { get; init; } = default!;
    public string? Route { get; init; }
    public string? ImageUrl { get; init; }
    public DateTime CreatedAt { get; init; }
    public bool IsRead { get; init; }
}

/// <summary>
/// The signed-in user's notification feed — MeDan's first-party channel, no
/// push provider involved. The app polls this; push merely arrives sooner.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly CurrentUser _current;

    public NotificationsController(AppDbContext db, CurrentUser current)
    {
        _db = db;
        _current = current;
    }

    /// <summary>Newest first, capped at 100.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<NotificationResponse>>> Mine(CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return Unauthorized("Register first.");

        var items = await _db.Notifications.AsNoTracking()
            .Where(n => n.UserId == me.Id)
            .OrderByDescending(n => n.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        return items.Select(n => new NotificationResponse
        {
            Id = n.Id,
            Title = n.Title,
            Body = n.Body,
            Type = n.Type,
            Route = n.Route,
            ImageUrl = n.ImageUrl,
            CreatedAt = n.CreatedAt,
            IsRead = n.ReadAt != null
        }).ToList();
    }

    /// <summary>How many are unread — drives the bell's badge.</summary>
    [HttpGet("unread-count")]
    public async Task<ActionResult<int>> UnreadCount(CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return Unauthorized("Register first.");
        return await _db.Notifications
            .CountAsync(n => n.UserId == me.Id && n.ReadAt == null, ct);
    }

    /// <summary>Marks everything read — called when the feed is opened.</summary>
    [HttpPost("read-all")]
    public async Task<IActionResult> ReadAll(CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return Unauthorized("Register first.");

        await _db.Notifications
            .Where(n => n.UserId == me.Id && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, DateTime.UtcNow), ct);
        return NoContent();
    }
}
