namespace Skarbiec.Portfolio.Features.ContributionLimits.SetContributionLimits;

public sealed record SetContributionLimitsRequest
{
    /// <summary>The year's IKE limit in PLN; null removes it.</summary>
    public decimal? IkeAmount { get; init; }

    /// <summary>The year's IKZE limit in PLN; null removes it.</summary>
    public decimal? IkzeAmount { get; init; }
}
