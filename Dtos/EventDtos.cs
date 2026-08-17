using System.ComponentModel.DataAnnotations;

namespace MeDan.Api.Dtos;

public record EventResponse
{
    public Guid Id { get; init; }
    public string Title { get; init; } = default!;
    public string? Description { get; init; }
    public string Venue { get; init; } = default!;

    /// <summary>Null means the event shows on every campus.</summary>
    public string? Campus { get; init; }

    public DateTime StartsAt { get; init; }
    public DateTime? EndsAt { get; init; }
    public string? ImageUrl { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>Body for POST/PUT /api/events — staff only.</summary>
public record SaveEventRequest
{
    [Required, MaxLength(150)] public string Title { get; init; } = default!;
    [MaxLength(2000)] public string? Description { get; init; }
    [Required, MaxLength(200)] public string Venue { get; init; } = default!;

    /// <summary>Campus code (UENR/USTED), or null for every campus.</summary>
    [MaxLength(20)] public string? Campus { get; init; }

    [Required] public DateTime StartsAt { get; init; }
    public DateTime? EndsAt { get; init; }
    [MaxLength(500)] public string? ImageUrl { get; init; }
}
