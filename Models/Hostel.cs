using System.ComponentModel.DataAnnotations;

namespace MeDan.Api.Models;

/// <summary>
/// A property listing — a hostel, hometel, apartment, etc. (see <see cref="PropertyType"/>).
/// Owned by a <see cref="Company"/>; contains many <see cref="Room"/>s.
/// </summary>
public class Hostel
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = default!;

    [MaxLength(150)]
    public string Name { get; set; } = default!;

    public PropertyType PropertyType { get; set; } = PropertyType.Hostel;

    [MaxLength(2000)]
    public string? Description { get; set; }

    [MaxLength(20)]
    public string CampusCode { get; set; } = default!;
    public Campus Campus { get; set; } = default!;

    [MaxLength(300)]
    public string Address { get; set; } = default!;

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    /// <summary>Distance from campus gate, km (denormalized for sorting).</summary>
    public double DistanceKm { get; set; }

    // Pricing range (denormalized from rooms for fast filtering), GH₵ per semester per bed.
    public int MinPrice { get; set; }
    public int MaxPrice { get; set; }

    public bool IsVerified { get; set; }

    // Review aggregates (denormalized).
    public double Rating { get; set; }
    public int ReviewCount { get; set; }

    [MaxLength(30)]
    public string? ContactPhone { get; set; }

    /// <summary>The owner/worker who created this listing.</summary>
    public Guid PostedByUserId { get; set; }
    public AppUser PostedBy { get; set; } = default!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<HostelPhoto> Photos { get; set; } = new List<HostelPhoto>();
    public ICollection<HostelAmenity> Amenities { get; set; } = new List<HostelAmenity>();
    public ICollection<Room> Rooms { get; set; } = new List<Room>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
}
