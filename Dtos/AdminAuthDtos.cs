using System.ComponentModel.DataAnnotations;
using MeDan.Api.Models;

namespace MeDan.Api.Dtos;

/// <summary>Creates a staff account that lives entirely in this API (no Firebase).</summary>
public record StaffRegisterRequest
{
    [Required, EmailAddress, MaxLength(256)] public string Email { get; init; } = default!;

    [Required, MinLength(10), MaxLength(128)] public string Password { get; init; } = default!;

    [Required, MaxLength(150)] public string Name { get; init; } = default!;

    [MaxLength(30)] public string? Phone { get; init; }

    /// <summary>
    /// Ignored for the bootstrap account (the first staff user is always Admin).
    /// Afterwards only an Admin may call this, and this is the role they grant.
    /// </summary>
    public UserRole Role { get; init; } = UserRole.Manager;
}

public record StaffLoginRequest
{
    [Required, EmailAddress, MaxLength(256)] public string Email { get; init; } = default!;
    [Required, MaxLength(128)] public string Password { get; init; } = default!;
}

/// <summary>A signed MeDan token plus the profile the dashboard renders.</summary>
public record StaffAuthResponse
{
    public string Token { get; init; } = default!;
    public DateTime ExpiresAt { get; init; }
    public UserResponse User { get; init; } = default!;
}
