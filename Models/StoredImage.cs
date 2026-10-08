using System.ComponentModel.DataAnnotations;

namespace MeDan.Api.Models;

/// <summary>
/// An uploaded image kept in the database itself.
///
/// Render's free tier erases the container disk on every deploy, which kept
/// deleting photos saved under wwwroot/uploads while their URLs lived on in
/// the database. Postgres is the one store this deployment has that survives
/// a deploy, so the bytes go here. Listing photos are a few hundred KB each
/// at the volumes this platform sees — fine for a table; revisit if uploads
/// ever outgrow the database plan.
/// </summary>
public class StoredImage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(100)]
    public string ContentType { get; set; } = default!;

    public byte[] Data { get; set; } = default!;

    /// <summary>
    /// Public rows are served by <c>GET /api/images/{id}</c> (hostel photos,
    /// avatars, event posters). Private rows are payment proofs — a MoMo
    /// receipt carries the student's name, number and balance, and is only
    /// readable through the authorized proof endpoint.
    /// </summary>
    public bool IsPublic { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
