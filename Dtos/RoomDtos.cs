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

/// <summary>
/// Body for PUT /api/hostels/{hostelId}/rooms/{roomId}.
///
/// Same meaning as create: <see cref="PricePerSemester"/> is the owner's ASKING
/// price and the server puts MeDan's 5% on top. The edit form loads
/// <see cref="RoomSummary.OwnerPrice"/> — not the listed price — so the number
/// goes out and comes back unchanged and the markup cannot compound.
///
/// Floor is only changed when provided, because clients don't round-trip it
/// (RoomSummary doesn't carry it).
/// </summary>
public record UpdateRoomRequest
{
    [Required, MaxLength(80)] public string Label { get; init; } = default!;
    public RoomType Type { get; init; } = RoomType.Single;

    /// <summary>Beds in the room: 1–4. Beds are added or (if free) removed to match.</summary>
    [Range(1, 4)] public int Capacity { get; init; } = 1;

    /// <summary>Owner's asking price per bed/space per semester, GH₵ — what they receive.</summary>
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

    /// <summary>What the STUDENT pays, MeDan's 5% included. Use this on the student app.</summary>
    public int PricePerSemester { get; init; }

    /// <summary>
    /// What the OWNER receives — the asking price they typed. Use this everywhere on
    /// the owner/admin side, and send it back as <c>pricePerSemester</c> when editing,
    /// which is the same number create takes.
    /// </summary>
    public int OwnerPrice { get; init; }
    public string Status { get; init; } = default!;        // available | occupied | maintenance
    public int Capacity { get; init; }

    // --- additive (per-bed model) ---
    public int AvailableBeds { get; init; }
    public string Gender { get; init; } = default!;
}
