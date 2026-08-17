using System.ComponentModel.DataAnnotations;

namespace MeDan.Api.Models;

/// <summary>
/// Extra details for a user whose role is Student.
/// One-to-one with <see cref="AppUser"/>.
/// </summary>
public class StudentProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK + PK link to the owning user.</summary>
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = default!;

    [MaxLength(150)]
    public string Course { get; set; } = default!;

    [MaxLength(150)]
    public string Department { get; set; } = default!;

    /// <summary>Academic level/year, e.g. "100", "200", "Level 300".</summary>
    [MaxLength(20)]
    public string? Level { get; set; }

    /// <summary>Campus code the student belongs to (FK to Campus).</summary>
    [MaxLength(20)]
    public string? CampusCode { get; set; }
    public Campus? Campus { get; set; }

    /// <summary>School index / student ID number.</summary>
    [MaxLength(50)]
    public string? IndexNumber { get; set; }

    // --- Parent / Guardian ---
    [MaxLength(150)]
    public string GuardianName { get; set; } = default!;

    [MaxLength(30)]
    public string GuardianPhone { get; set; } = default!;

    /// <summary>Relationship to student, e.g. "Parent", "Guardian", "Sibling".</summary>
    [MaxLength(50)]
    public string GuardianRelationship { get; set; } = "Parent";

    [MaxLength(256)]
    public string? GuardianEmail { get; set; }
}
