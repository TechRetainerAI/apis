using System.ComponentModel.DataAnnotations;

namespace MeDan.Api.Models;

/// <summary>A facility a hostel can offer, e.g. WiFi, AC, Generator. Many-to-many with Hostel.</summary>
public class Amenity
{
    public int Id { get; set; }

    [MaxLength(50)]
    public string Name { get; set; } = default!;

    /// <summary>Icon key the app maps to an icon, e.g. "wifi".</summary>
    [MaxLength(50)]
    public string? IconKey { get; set; }

    public ICollection<HostelAmenity> Hostels { get; set; } = new List<HostelAmenity>();
}

/// <summary>Join row for the Hostel &lt;-&gt; Amenity many-to-many.</summary>
public class HostelAmenity
{
    public Guid HostelId { get; set; }
    public Hostel Hostel { get; set; } = default!;

    public int AmenityId { get; set; }
    public Amenity Amenity { get; set; } = default!;
}
