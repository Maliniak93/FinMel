using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;

namespace Skarbiec.MarketData.Tests.Fixtures.PriceSources;

/// <summary>
/// Canned <see cref="IPriceSource"/> — one scripted <see cref="PriceFetchResult{TValue}"/> per method,
/// proving a caller's (job's) handling of the outcome rather than a vendor parser, unlike
/// <see cref="FixturePriceSource"/> (T2.2). <see cref="PriceSyncJob"/> (T2.6) exercises only
/// <see cref="latestResult"/>; <see cref="HistoryBackfillJob"/> (T2.7) exercises only
/// <see cref="historyResult"/> — whichever a test doesn't script throws if called, so an accidental
/// call shows up as a test failure instead of a silent null/default.
/// </summary>
public sealed class ScriptedPriceSource(
    PriceSource source,
    PriceFetchResult<InstrumentQuote>? latestResult = null,
    PriceFetchResult<InstrumentQuote>? historyResult = null) : IPriceSource
{
    public PriceSource Source { get; } = source;

    public TimeSpan RequestDelay => TimeSpan.Zero;

    public int HistoryFetchCount { get; private set; }

    public Task<PriceFetchResult<InstrumentQuote>> FetchLatestAsync(
        IReadOnlyCollection<Instrument> instruments, CancellationToken cancellationToken)
    {
        if (latestResult is null)
        {
            throw new NotSupportedException($"This {nameof(ScriptedPriceSource)} wasn't given a latestResult.");
        }

        return Task.FromResult(latestResult);
    }

    public Task<PriceFetchResult<InstrumentQuote>> FetchHistoryAsync(
        Instrument instrument, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        HistoryFetchCount++;

        if (historyResult is null)
        {
            throw new NotSupportedException($"This {nameof(ScriptedPriceSource)} wasn't given a historyResult.");
        }

        return Task.FromResult(historyResult);
    }
}
