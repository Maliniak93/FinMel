using System.Collections.Concurrent;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Tests.Fixtures;

/// <summary>
/// Stands in for <see cref="IFxRateLookupClient"/> in HTTP slice tests — the Portfolio test host has
/// no MarketData Testcontainer to call, so the "resolve the transaction-date rate to PLN" step of
/// RecordTransaction/UpdateTransaction/AddAsset (transactions-pln-value-and-fee-removal) is
/// substituted here. Every currency defaults to <see cref="DefaultRate"/> so a test only opts in to
/// the outcome it is exercising, and every call is recorded so a fact can prove MarketData was
/// never asked (PLN assets, archived portfolios).
/// </summary>
public sealed class FakeFxRateLookupClient : IFxRateLookupClient
{
    /// <summary>The rate every currency answers with unless a test overrides it.</summary>
    public const decimal DefaultRate = 1m;

    private readonly ConcurrentDictionary<string, FxRateLookupResult> _byCurrency = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<(string Currency, DateOnly Date), FxRateLookupResult> _byCurrencyAndDate = new();
    private readonly ConcurrentQueue<(string Currency, DateOnly Date)> _calls = new();

    /// <summary>Every <c>GetRateAsync</c> call so far, in arrival order.</summary>
    public IReadOnlyList<(string Currency, DateOnly Date)> Calls => [.. _calls];

    /// <summary>Answers <paramref name="rate"/> for every date of <paramref name="currency"/> without a date-specific override.</summary>
    public FakeFxRateLookupClient WithRate(string currency, decimal rate)
    {
        _byCurrency[currency] = FxRateLookupResult.Found(rate);
        return this;
    }

    /// <summary>Answers <paramref name="rate"/> for exactly <paramref name="currency"/> on <paramref name="date"/>; wins over <see cref="WithRate(string, decimal)"/>.</summary>
    public FakeFxRateLookupClient WithRate(string currency, DateOnly date, decimal rate)
    {
        _byCurrencyAndDate[(currency.ToUpperInvariant(), date)] = FxRateLookupResult.Found(rate);
        return this;
    }

    /// <summary>MarketData has no rate for <paramref name="currency"/> on or before any date asked.</summary>
    public FakeFxRateLookupClient WithNotFound(string currency)
    {
        _byCurrency[currency] = FxRateLookupResult.NotFound;
        return this;
    }

    /// <summary>MarketData is unreachable whenever <paramref name="currency"/> is asked for.</summary>
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
