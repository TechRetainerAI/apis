using System.ComponentModel.DataAnnotations;

namespace MeDan.Api.Dtos;

public record CreateReviewRequest
{
    [Range(1, 5)] public int Rating { get; init; }
    [MaxLength(1000)] public string? Comment { get; init; }
}

public record ReviewResponse
{
    public Guid Id { get; init; }
    public Guid HostelId { get; init; }
    public Guid StudentUserId { get; init; }
    public string StudentName { get; init; } = default!;
    public string? StudentPhotoUrl { get; init; }
    public int Rating { get; init; }
    public string? Comment { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>Body for PUT /api/auth/me — the fields a student may edit themselves.</summary>
public record UpdateProfileRequest
{
    [Required, MaxLength(150)] public string Name { get; init; } = default!;
    [MaxLength(30)] public string? Phone { get; init; }
}

/// <summary>Body for POST /api/auth/me/password.</summary>
public record ChangePasswordRequest
{
    [Required, MaxLength(128)] public string CurrentPassword { get; init; } = default!;
    [Required, MinLength(6), MaxLength(128)] public string NewPassword { get; init; } = default!;
}
