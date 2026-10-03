using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;

namespace Skarbiec.MarketData.Tests.Fixtures.PriceSources;

// A method the test did not script throws, so an accidental call fails the test instead of returning a default.
public sealed class ScriptedPriceSource(
    PriceSource source,
    PriceFetchResult<InstrumentQuote>? latestResult = null,
    PriceFetchResult<InstrumentQuote>? historyResult = null) : IPriceSource
{
    public PriceSource Source { get; } = source;

    public TimeSpan RequestDelay => TimeSpan.Zero;

    private readonly List<Guid> _latestFetchedInstrumentIds = [];

    public int HistoryFetchCount { get; private set; }

    // The scripted result ignores the arguments, so a "which instruments were asked for" assertion reads them from here.
    public IReadOnlyList<Guid> LatestFetchedInstrumentIds => _latestFetchedInstrumentIds;

    public Task<PriceFetchResult<InstrumentQuote>> FetchLatestAsync(
        IReadOnlyCollection<Instrument> instruments, CancellationToken cancellationToken)
    {
        _latestFetchedInstrumentIds.AddRange(instruments.Select(i => i.Id));

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
