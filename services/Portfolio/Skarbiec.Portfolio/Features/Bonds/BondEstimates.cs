using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.Bonds;

internal sealed record BondEstimateInput(BondResponse Response, TreasuryBond Terms, IReadOnlyCollection<BondInterestSettlement> Settlements);

internal static class BondEstimates
{
    private static readonly IReadOnlyDictionary<int, decimal> NoRates = new Dictionary<int, decimal>();

    // One batch call for the distinct series whose rates are not all local; a MarketData outage only blanks the bonds that needed it.
    public static async Task<List<BondResponse>> WithEstimatesAsync(
        this IBondRateLookupClient rateLookup, IReadOnlyList<BondEstimateInput> bonds, DateOnly today, CancellationToken cancellationToken)
    {
        var valued = bonds
            .Select(b => (
                Bond: b,
                IsValued: b.Response.Status != BondStatus.Redeemed,
                NeedsCatalog: b.Response.Status != BondStatus.Redeemed && BondValuation.NeedsCatalogRates(b.Terms, b.Settlements, today)))
            .ToList();

        var codes = valued
            .Where(v => v.NeedsCatalog)
            .Select(v => v.Bond.Terms.SeriesCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var lookup = codes.Count == 0
            ? BondRateLookupResult.Found(new Dictionary<string, IReadOnlyDictionary<int, decimal>>())
            : await rateLookup.GetRatesAsync(codes, cancellationToken);

        return valued
            .Select(v =>
            {
                var outcome = !v.IsValued ? BondValuationOutcome.None
                    : v.NeedsCatalog && lookup.Status == BondRateLookupStatus.Unavailable
                        ? BondValuationOutcome.Unavailable(BondEstimateUnavailableReason.MarketDataUnavailable)
                    : BondValuation.Today(
                        v.Bond.Terms, v.Bond.Settlements, lookup.Rates?.GetValueOrDefault(v.Bond.Terms.SeriesCode) ?? NoRates, today);

                return v.Bond.Response with { Estimate = outcome.Estimate, EstimateUnavailableReason = outcome.Reason };
            })
            .ToList();
    }
}
