using Skarbiec.MarketData.Sources;

namespace Skarbiec.MarketData.Tests.Fixtures.PriceSources;

/// <summary>FX counterpart to <see cref="ScriptedPriceSource"/> — see its doc comment.</summary>
public sealed class ScriptedFxRateSource(
    PriceFetchResult<FxRateQuote>? latestResult = null,
    PriceFetchResult<FxRateQuote>? historyResult = null) : IFxRateSource
{
    public TimeSpan RequestDelay => TimeSpan.Zero;

    public int HistoryFetchCount { get; private set; }

    public Task<PriceFetchResult<FxRateQuote>> FetchLatestAsync(
        IReadOnlyCollection<string> currencyCodes, CancellationToken cancellationToken)
    {
        if (latestResult is null)
        {
            throw new NotSupportedException($"This {nameof(ScriptedFxRateSource)} wasn't given a latestResult.");
        }

        return Task.FromResult(latestResult);
    }

    public Task<PriceFetchResult<FxRateQuote>> FetchHistoryAsync(
        string currencyCode, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        HistoryFetchCount++;

        if (historyResult is null)
        {
            throw new NotSupportedException($"This {nameof(ScriptedFxRateSource)} wasn't given a historyResult.");
        }

        return Task.FromResult(historyResult);
    }
}
