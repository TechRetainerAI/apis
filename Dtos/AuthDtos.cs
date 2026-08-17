using System.ComponentModel.DataAnnotations;
using MeDan.Api.Models;

namespace MeDan.Api.Dtos;

/// <summary>
/// Body for POST /api/auth/register. Identity lives in this API: the password is
/// stored as a PBKDF2 hash and registration returns a MeDan-signed JWT.
/// </summary>
public record RegisterRequest
{
    [Required, EmailAddress, MaxLength(256)] public string Email { get; init; } = default!;

    [Required, MinLength(6), MaxLength(128)] public string Password { get; init; } = default!;

    [Required, MaxLength(150)] public string Name { get; init; } = default!;
    [MaxLength(30)] public string? Phone { get; init; }

    /// <summary>
    /// Self-service registration only ever creates Student accounts — staff and owner
    /// roles are granted by an Admin afterwards, so this is not settable here.
    /// </summary>
    public StudentInfo? Student { get; init; }
}

/// <summary>Body for POST /api/auth/login — used by the app and the admin dashboard alike.</summary>
public record LoginRequest
{
    [Required, EmailAddress, MaxLength(256)] public string Email { get; init; } = default!;
    [Required, MaxLength(128)] public string Password { get; init; } = default!;
}

/// <summary>A signed MeDan token plus the caller's profile.</summary>
public record AuthResponse
{
    public string Token { get; init; } = default!;
    public DateTime ExpiresAt { get; init; }
    public UserResponse User { get; init; } = default!;
}

public record StudentInfo
{
    [Required, MaxLength(150)] public string Course { get; init; } = default!;
    [Required, MaxLength(150)] public string Department { get; init; } = default!;
    [MaxLength(20)] public string? Level { get; init; }
    [MaxLength(20)] public string? CampusCode { get; init; }
    [MaxLength(50)] public string? IndexNumber { get; init; }

    [Required, MaxLength(150)] public string GuardianName { get; init; } = default!;
    [Required, MaxLength(30)] public string GuardianPhone { get; init; } = default!;
    [MaxLength(50)] public string GuardianRelationship { get; init; } = "Parent";
    [MaxLength(256)] public string? GuardianEmail { get; init; }
}

public record UserResponse
{
    public Guid Id { get; init; }
    public string Email { get; init; } = default!;
    public string Name { get; init; } = default!;          // app key: "name"
    public string? Phone { get; init; }
    public string? PhotoUrl { get; init; }
    public string Role { get; init; } = default!;
    public StudentInfo? Student { get; init; }
}
