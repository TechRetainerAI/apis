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

    /// <summary>
    /// This room's own images, cover first. Empty when none have been uploaded —
    /// show the hostel's gallery instead.
    /// </summary>
    public List<string> Photos { get; init; } = new();

    /// <summary>
    /// Same photos with their ids, so a manage screen can delete a specific one.
    /// Mirrors <c>HostelDetail.PhotoItems</c>.
    /// </summary>
    public List<PhotoResponse> PhotoItems { get; init; } = new();
}

/// <summary>
/// Free beds in a hostel grouped the way a student actually chooses: how many
/// share the room, and who it is for.
///
/// Saves the app grouping the room list itself, and answers "is there a space in
/// a two-in-a-room for a girl?" in one line rather than by reading every room.
/// </summary>
public record AvailabilityGroup
{
    /// <summary>camelCase room type, e.g. "doublyShared".</summary>
    public string Type { get; init; } = default!;

    /// <summary>How many beds the room sleeps — 1, 2, 3 or 4.</summary>
    public int Capacity { get; init; }

    /// <summary>camelCase gender, e.g. "female".</summary>
    public string Gender { get; init; } = default!;

    /// <summary>Rooms of this kind with at least one bed free.</summary>
    public int RoomsWithSpace { get; init; }

    /// <summary>Free beds across all of them.</summary>
    public int FreeBeds { get; init; }

    /// <summary>Every room of this kind, free or not.</summary>
    public int TotalRooms { get; init; }

    /// <summary>Cheapest student price among the rooms with space, GH₵. Null if none.</summary>
    public int? FromPrice { get; init; }
}
