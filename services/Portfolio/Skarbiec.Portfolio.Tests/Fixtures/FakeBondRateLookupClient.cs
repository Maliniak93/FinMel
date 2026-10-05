using System.Collections.Concurrent;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Tests.Fixtures;

public sealed class FakeBondRateLookupClient : IBondRateLookupClient
{
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<int, decimal>> _bySeries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<IReadOnlyList<string>> _calls = new();
    private volatile bool _unavailable;

    public IReadOnlyList<IReadOnlyList<string>> Calls => [.. _calls];

    // PeriodIndex is 1-based, as in BondPeriod.Index.
    public FakeBondRateLookupClient WithRates(string seriesCode, params (int PeriodIndex, decimal RatePercent)[] rates)
    {
        _bySeries[seriesCode] = rates.ToDictionary(r => r.PeriodIndex, r => r.RatePercent);
        return this;
    }

    public FakeBondRateLookupClient WithUnavailable()
    {
        _unavailable = true;
        return this;
    }

    public Task<BondRateLookupResult> GetRatesAsync(IReadOnlyCollection<string> seriesCodes, CancellationToken cancellationToken)
    {
        _calls.Enqueue([.. seriesCodes]);

        if (_unavailable)
        {
            return Task.FromResult(BondRateLookupResult.Unavailable);
        }

        var found = seriesCodes
            .Where(_bySeries.ContainsKey)
            .ToDictionary(code => code, code => _bySeries[code], StringComparer.OrdinalIgnoreCase);

        return Task.FromResult(BondRateLookupResult.Found(found));
    }
}
