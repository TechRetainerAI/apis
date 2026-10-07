namespace MeDan.Api.Helpers;

/// <summary>
/// MeDan's cut is added ON TOP of the owner's asking price when a listing is
/// created: the owner types GH₵1000, students see and pay GH₵1050, and the
/// payout returns the owner's full 1000. Stored prices therefore always
/// INCLUDE the markup; <see cref="PlatformShare"/> gets MeDan's slice back
/// out of a charged amount.
/// </summary>
public static class Pricing
{
    /// <summary>Fraction added on top of the owner's asking price.</summary>
    public const decimal MarkupRate = 0.05m;

    /// <summary>The student-facing price for an owner's asking price.</summary>
    public static int WithMarkup(int ownerPrice) =>
        (int)Math.Round(ownerPrice * (1 + MarkupRate), MidpointRounding.AwayFromZero);

    /// <summary>
    /// MeDan's share embedded in a listed amount. Computed as listed − owner
    /// rather than listed × 5% so that, whatever the rounding did at listing
    /// time, the owner's payout lands back on their asking price.
    /// </summary>
    public static int PlatformShare(int listedAmount) =>
        listedAmount - (int)Math.Round(listedAmount / (1 + MarkupRate), MidpointRounding.AwayFromZero);
}
