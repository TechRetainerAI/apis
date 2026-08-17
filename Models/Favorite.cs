namespace MeDan.Api.Models;

/// <summary>A student's saved/favorited hostel. Unique per (student, hostel).</summary>
public class Favorite
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid StudentUserId { get; set; }
    public AppUser Student { get; set; } = default!;

    public Guid HostelId { get; set; }
    public Hostel Hostel { get; set; } = default!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
