using System.ComponentModel.DataAnnotations;
using MeDan.Api.Models;

namespace MeDan.Api.Dtos;

public record CreateRoomRequest
{
    [Required, MaxLength(80)] public string Label { get; init; } = default!;

    /// <summary>App key: "type". single | doublyShared | triplyShared | quadShared | ensuite | apartment.</summary>
    public RoomType Type { get; init; } = RoomType.Single;

    /// <summary>Beds in the room: 1, 2, 3, or 4. Beds are auto-created.</summary>
    [Range(1, 4)] public int Capacity { get; init; } = 1;

    /// <summary>Price per bed/space per semester, GH₵ (app key: "pricePerSemester").</summary>
    [Range(0, int.MaxValue)] public int PricePerSemester { get; init; }
    public Gender Gender { get; init; } = Gender.Mixed;
    [MaxLength(30)] public string? Floor { get; init; }
}

/// <summary>Body for PUT /api/hostels/{hostelId}/rooms/{roomId}/status.</summary>
public record SetRoomStatusRequest
{
    /// <summary>available | occupied | maintenance.</summary>
    [Required] public RoomStatus Status { get; init; }
}

/// <summary>Mirrors the app's <c>RoomModel</c> contract; availableBeds/gender are additive.</summary>
public record RoomSummary
{
    public Guid Id { get; init; }
    public Guid HostelId { get; init; }
    public string Label { get; init; } = default!;
    public string Type { get; init; } = default!;          // camelCase enum, e.g. "doublyShared"
    public int PricePerSemester { get; init; }
    public string Status { get; init; } = default!;        // available | occupied | maintenance
    public int Capacity { get; init; }

    // --- additive (per-bed model) ---
    public int AvailableBeds { get; init; }
    public string Gender { get; init; } = default!;
}
