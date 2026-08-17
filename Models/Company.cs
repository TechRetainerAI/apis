using System.ComponentModel.DataAnnotations;

namespace MeDan.Api.Models;

/// <summary>
/// A hostel business / organization. It owns hostels. Its owner and workers
/// (see <see cref="CompanyMember"/>) can post and manage listings under it.
/// </summary>
public class Company
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(150)]
    public string Name { get; set; } = default!;

    /// <summary>The owner (an AppUser with role Owner).</summary>
    public Guid OwnerUserId { get; set; }
    public AppUser Owner { get; set; } = default!;

    [MaxLength(30)]
    public string? Phone { get; set; }

    [MaxLength(256)]
    public string? Email { get; set; }

    [MaxLength(300)]
    public string? Address { get; set; }

    public bool IsVerified { get; set; }

    public CompanyTier Tier { get; set; } = CompanyTier.Starter;

    /// <summary>Commission fraction taken per booking (e.g. 0.07). Derived from <see cref="Tier"/>.</summary>
    public decimal CommissionRate { get; set; } = 0.07m;

    /// <summary>Max active listings for Starter tier; null = unlimited.</summary>
    public int? ListingLimit { get; set; } = 5;

    // ---------- Settlement (where escrow is released to) ----------

    /// <summary>Paystack bank code, e.g. "MTN" for MoMo or a bank's numeric code.</summary>
    [MaxLength(20)] public string? SettlementBankCode { get; set; }

    /// <summary>Account or mobile-money number the payout is sent to.</summary>
    [MaxLength(30)] public string? SettlementAccountNumber { get; set; }

    /// <summary>Account holder's name, as resolved by Paystack — not user-supplied.</summary>
    [MaxLength(150)] public string? SettlementAccountName { get; set; }

    /// <summary>
    /// Paystack transfer-recipient code ("RCP_..."). Created once the account is
    /// resolved; transfers reference this rather than raw account details.
    /// </summary>
    [MaxLength(100)] public string? PaystackRecipientCode { get; set; }

    /// <summary>True once an account has been resolved and a recipient created.</summary>
    public bool CanReceivePayouts =>
        !string.IsNullOrWhiteSpace(PaystackRecipientCode);

    public DateTime? SettlementUpdatedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<CompanyMember> Members { get; set; } = new List<CompanyMember>();
    public ICollection<Hostel> Hostels { get; set; } = new List<Hostel>();
}
