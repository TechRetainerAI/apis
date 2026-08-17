using System.ComponentModel.DataAnnotations;

namespace MeDan.Api.Models;

/// <summary>University/campus master data. Hostels and students reference a campus by code.</summary>
public class Campus
{
    /// <summary>Short code, e.g. "UENR", "USTED". Primary key.</summary>
    [MaxLength(20)]
    public string Code { get; set; } = default!;

    [MaxLength(200)]
    public string FullName { get; set; } = default!;

    [MaxLength(100)]
    public string City { get; set; } = default!;

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    public ICollection<Hostel> Hostels { get; set; } = new List<Hostel>();
}
