using System.Collections.Concurrent;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Tests.Fixtures;

public sealed class FakeFxRateLookupClient : IFxRateLookupClient
{
    public const decimal DefaultRate = 1m;

    private readonly ConcurrentDictionary<string, FxRateLookupResult> _byCurrency = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<(string Currency, DateOnly Date), FxRateLookupResult> _byCurrencyAndDate = new();
    private readonly ConcurrentQueue<(string Currency, DateOnly Date)> _calls = new();

    public IReadOnlyList<(string Currency, DateOnly Date)> Calls => [.. _calls];

    public FakeFxRateLookupClient WithRate(string currency, decimal rate)
    {
        _byCurrency[currency] = FxRateLookupResult.Found(rate);
        return this;
    }

    public FakeFxRateLookupClient WithRate(string currency, DateOnly date, decimal rate)
    {
        _byCurrencyAndDate[(currency.ToUpperInvariant(), date)] = FxRateLookupResult.Found(rate);
        return this;
    }

    public FakeFxRateLookupClient WithNotFound(string currency)
    {
        _byCurrency[currency] = FxRateLookupResult.NotFound;
        return this;
    }

    public FakeFxRateLookupClient WithUnavailable(string currency)
    {
        _byCurrency[currency] = FxRateLookupResult.Unavailable;
        return this;
    }

    public Task<FxRateLookupResult> GetRateAsync(string currency, DateOnly date, CancellationToken cancellationToken)
    {
        _calls.Enqueue((currency, date));

        if (_byCurrencyAndDate.TryGetValue((currency.ToUpperInvariant(), date), out var exact))
        {
            return Task.FromResult(exact);
        }

        return Task.FromResult(_byCurrency.GetValueOrDefault(currency, FxRateLookupResult.Found(DefaultRate)));
    }
}
