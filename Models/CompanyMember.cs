namespace MeDan.Api.Models;

/// <summary>
/// Join row linking an <see cref="AppUser"/> to a <see cref="Company"/>.
/// Models the owner + workers who can act on the company's listings.
/// </summary>
public class CompanyMember
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = default!;

    public Guid UserId { get; set; }
    public AppUser User { get; set; } = default!;

    public CompanyRole Role { get; set; } = CompanyRole.Worker;

    /// <summary>Whether this member may create/edit hostel listings.</summary>
    public bool CanPostListings { get; set; } = true;

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}
