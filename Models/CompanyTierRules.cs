namespace MeDan.Api.Models;

/// <summary>Commission rate + listing limit for each subscription tier (single source of truth).</summary>
public static class CompanyTierRules
{
    /// <returns>(commissionRate, listingLimit) — listingLimit null = unlimited.</returns>
    public static (decimal CommissionRate, int? ListingLimit) For(CompanyTier tier) => tier switch
    {
        CompanyTier.Pro => (0.055m, null),
        CompanyTier.Premium => (0.050m, null),
        _ => (0.070m, 5) // Starter
    };

    /// <summary>Applies the tier's commission + listing limit onto a company.</summary>
    public static void Apply(this Company company, CompanyTier tier)
    {
        company.Tier = tier;
        (company.CommissionRate, company.ListingLimit) = For(tier);
    }
}
