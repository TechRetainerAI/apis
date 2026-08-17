using MeDan.Api.Auth;
using MeDan.Api.Data;
using MeDan.Api.Dtos;
using MeDan.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MeDan.Api.Controllers;

/// <summary>
/// Student reviews of a hostel. The hostel's Rating/ReviewCount are kept in step
/// on every write so listings don't need to aggregate on read.
/// </summary>
[ApiController]
[Route("api/hostels/{hostelId:guid}/reviews")]
public class ReviewsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly CurrentUser _current;

    public ReviewsController(AppDbContext db, CurrentUser current)
    {
        _db = db;
        _current = current;
    }

    /// <summary>Reviews for a hostel, newest first.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<ReviewResponse>>> List(
        Guid hostelId,
        [FromQuery] int? rating,
        [FromQuery] string? sort,
        CancellationToken ct = default)
    {
        var query = _db.Reviews.AsNoTracking()
            .Include(r => r.Student)
            .Where(r => r.HostelId == hostelId);

        if (rating is >= 1 and <= 5) query = query.Where(r => r.Rating == rating);

        query = sort switch
        {
            "oldest" => query.OrderBy(r => r.CreatedAt),
            "top" => query.OrderByDescending(r => r.Rating).ThenByDescending(r => r.CreatedAt),
            _ => query.OrderByDescending(r => r.CreatedAt),
        };

        var items = await query.Take(200).ToListAsync(ct);
        return items.Select(ToResponse).ToList();
    }

    /// <summary>
    /// Leave or replace the caller's review. One review per student per hostel —
    /// posting again edits the existing one rather than stacking duplicates.
    /// </summary>
    [HttpPost]
    [Authorize]
    public async Task<ActionResult<ReviewResponse>> Create(
        Guid hostelId, CreateReviewRequest req, CancellationToken ct)
    {
        var me = await _current.GetAsync(ct: ct);
        if (me is null) return Unauthorized("Register first.");

        var hostel = await _db.Hostels.FirstOrDefaultAsync(h => h.Id == hostelId, ct);
        if (hostel is null) return NotFound("Hostel not found.");

        var review = await _db.Reviews.FirstOrDefaultAsync(
            r => r.HostelId == hostelId && r.StudentUserId == me.Id, ct);

        if (review is null)
        {
            review = new Review
            {
                HostelId = hostelId,
                StudentUserId = me.Id,
                Rating = req.Rating,
                Comment = req.Comment
            };
            _db.Reviews.Add(review);
        }
        else
        {
            review.Rating = req.Rating;
            review.Comment = req.Comment;
            review.CreatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        await RefreshAggregateAsync(hostel, ct);

        // Re-read so the response carries the student's name.
        await _db.Entry(review).Reference(r => r.Student).LoadAsync(ct);
        return ToResponse(review);
    }

    /// <summary>Recomputes the hostel's cached rating and review count.</summary>
    private async Task RefreshAggregateAsync(Hostel hostel, CancellationToken ct)
    {
        var stats = await _db.Reviews
            .Where(r => r.HostelId == hostel.Id)
            .GroupBy(r => 1)
            .Select(g => new { Count = g.Count(), Avg = g.Average(r => (double)r.Rating) })
            .FirstOrDefaultAsync(ct);

        hostel.ReviewCount = stats?.Count ?? 0;
        hostel.Rating = stats is null ? 0 : Math.Round(stats.Avg, 1);
        await _db.SaveChangesAsync(ct);
    }

    private static ReviewResponse ToResponse(Review r) => new()
    {
        Id = r.Id,
        HostelId = r.HostelId,
        StudentUserId = r.StudentUserId,
        StudentName = r.Student?.FullName ?? "Student",
        StudentPhotoUrl = r.Student?.PhotoUrl,
        Rating = r.Rating,
        Comment = r.Comment,
        CreatedAt = r.CreatedAt
    };
}
