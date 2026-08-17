using System.ComponentModel.DataAnnotations;

namespace MeDan.Api.Models;

/// <summary>
/// A platform user, from either of two identity sources:
/// <list type="bullet">
/// <item>Students/owners from the mobile app — credentials live in Firebase Auth and this
/// row mirrors that user, keyed by <see cref="FirebaseUid"/>.</item>
/// <item>Platform staff from the admin dashboard — credentials live here as a
/// <see cref="PasswordHash"/>, with no Firebase account at all.</item>
/// </list>
/// Exactly one of the two is set for any given user.
/// </summary>
public class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Firebase Auth UID — the link between Firebase and our DB. Unique when present;
    /// null for staff accounts created through the admin API.
    /// </summary>
    [MaxLength(128)]
    public string? FirebaseUid { get; set; }

    /// <summary>
    /// PBKDF2 hash for API-native (staff) accounts. Null for Firebase-backed users,
    /// who must never be able to sign in with a password here.
    /// </summary>
    [MaxLength(256)]
    public string? PasswordHash { get; set; }

    [MaxLength(256)]
    public string Email { get; set; } = default!;

    [MaxLength(150)]
    public string FullName { get; set; } = default!;

    [MaxLength(30)]
    public string? Phone { get; set; }

    [MaxLength(500)]
    public string? PhotoUrl { get; set; }

    public UserRole Role { get; set; } = UserRole.Student;

    /// <summary>
    /// Canonical share code for "Refer &amp; Earn", e.g. "ABC123". Created lazily on first
    /// visit to <c>GET /api/referrals/me</c>. Unique across users.
    /// </summary>
    [MaxLength(20)]
    public string? ReferralCode { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public StudentProfile? StudentProfile { get; set; }
    public ICollection<CompanyMember> CompanyMemberships { get; set; } = new List<CompanyMember>();
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}
