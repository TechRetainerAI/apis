using System.ComponentModel.DataAnnotations;
using MeDan.Api.Models;

namespace MeDan.Api.Dtos;

public record CreateHostelRequest
{
    /// <summary>Optional. If omitted, the caller's company is used (auto-created on first listing).</summary>
    public Guid? CompanyId { get; init; }
    [Required, MaxLength(150)] public string Name { get; init; } = default!;
    public PropertyType PropertyType { get; init; } = PropertyType.Hostel;
    [MaxLength(2000)] public string? Description { get; init; }
    [Required, MaxLength(20)] public string Campus { get; init; } = default!;   // campus code
    [Required, MaxLength(300)] public string Address { get; init; } = default!;
    public double Lat { get; init; }
    public double Lng { get; init; }
    public double DistanceKm { get; init; }
    public int MinPrice { get; init; }
    public int MaxPrice { get; init; }
    [MaxLength(30)] public string? ContactPhone { get; init; }
    /// <summary>Amenity icon keys, e.g. ["wifi","ac"]. Unknown keys are ignored.</summary>
    public List<string> Amenities { get; init; } = new();
    /// <summary>Already-hosted photo URLs (optional; the app uploads files via /photos instead).</summary>
    public List<string> PhotoUrls { get; init; } = new();
}

/// <summary>
/// Body for PUT /api/hostels/{id}. Every field is replaced, so send the full record —
/// the dashboard loads the hostel, edits, and submits it back.
/// Photos are managed separately via the /photos routes.
/// </summary>
public record UpdateHostelRequest
{
    [Required, MaxLength(150)] public string Name { get; init; } = default!;
    public PropertyType PropertyType { get; init; } = PropertyType.Hostel;
    [MaxLength(2000)] public string? Description { get; init; }
    [Required, MaxLength(20)] public string Campus { get; init; } = default!;
    [Required, MaxLength(300)] public string Address { get; init; } = default!;
    public double Lat { get; init; }
    public double Lng { get; init; }
    public double DistanceKm { get; init; }
    public int MinPrice { get; init; }
    public int MaxPrice { get; init; }
    [MaxLength(30)] public string? ContactPhone { get; init; }
    /// <summary>Replaces the amenity set entirely. Unknown keys are ignored.</summary>
    public List<string> Amenities { get; init; } = new();
}

/// <summary>
/// Field names + types mirror the Flutter app's <c>HostelModel</c> JSON contract
/// (name, campus code, ownerId, lat/lng, photos, amenities). Extra fields
/// (propertyType, companyId) are additive — the app ignores keys it doesn't read.
/// </summary>
public record HostelSummary
{
    public Guid Id { get; init; }
    public string Name { get; init; } = default!;
    public string Campus { get; init; } = default!;        // campus code, e.g. "UENR"
    public Guid OwnerId { get; init; }                     // company owner's user id
    public string Address { get; init; } = default!;
    public double Lat { get; init; }
    public double Lng { get; init; }
    public double DistanceKm { get; init; }
    public int MinPrice { get; init; }
    public int MaxPrice { get; init; }
    public List<string> Photos { get; init; } = new();
    public List<string> Amenities { get; init; } = new();  // icon keys, e.g. ["wifi","ac"]
    public bool IsVerified { get; init; }
    public double Rating { get; init; }
    public int ReviewCount { get; init; }
    public string? Description { get; init; }
    public string? ContactPhone { get; init; }

    // --- additive (new features, app ignores until wired) ---
    public string PropertyType { get; init; } = default!;  // hostel | hometel | apartment | ...
    public Guid CompanyId { get; init; }
}

public record HostelDetail : HostelSummary
{
    public List<RoomSummary> Rooms { get; init; } = new();

    /// <summary>
    /// The same photos as <see cref="HostelSummary.Photos"/>, but with ids so a
    /// management UI can delete a specific one. Additive — the app reads `photos`.
    /// </summary>
    public List<PhotoResponse> PhotoItems { get; init; } = new();
}
