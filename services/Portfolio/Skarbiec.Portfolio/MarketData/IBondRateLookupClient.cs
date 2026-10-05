namespace Skarbiec.Portfolio.MarketData;

public enum BondRateLookupStatus
{
    Found,
    Unavailable,
}

/// <summary>Rates holds, per series code, the catalog's annual rate in percent keyed by the 1-based period index; a series MarketData does not know is absent.</summary>
public sealed record BondRateLookupResult(BondRateLookupStatus Status, IReadOnlyDictionary<string, IReadOnlyDictionary<int, decimal>>? Rates)
{
    public static BondRateLookupResult Unavailable { get; } = new(BondRateLookupStatus.Unavailable, null);

    public static BondRateLookupResult Found(IReadOnlyDictionary<string, IReadOnlyDictionary<int, decimal>> rates) =>
        new(BondRateLookupStatus.Found, rates);
}

public interface IBondRateLookupClient
{
    Task<BondRateLookupResult> GetRatesAsync(IReadOnlyCollection<string> seriesCodes, CancellationToken cancellationToken);
}
