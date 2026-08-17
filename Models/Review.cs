using System.ComponentModel.DataAnnotations;

namespace MeDan.Api.Models;

/// <summary>A student's rating + comment for a hostel.</summary>
public class Review
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid HostelId { get; set; }
    public Hostel Hostel { get; set; } = default!;

    public Guid StudentUserId { get; set; }
    public AppUser Student { get; set; } = default!;

    /// <summary>1–5 stars.</summary>
    public int Rating { get; set; }

    [MaxLength(1000)]
    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
