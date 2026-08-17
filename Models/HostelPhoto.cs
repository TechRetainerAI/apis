using System.ComponentModel.DataAnnotations;

namespace MeDan.Api.Models;

/// <summary>An image for a hostel (stored externally, e.g. Firebase Storage/blob; URL kept here).</summary>
public class HostelPhoto
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid HostelId { get; set; }
    public Hostel Hostel { get; set; } = default!;

    [MaxLength(500)]
    public string Url { get; set; } = default!;

    public bool IsCover { get; set; }
    public int SortOrder { get; set; }
}
