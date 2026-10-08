using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;

namespace Skarbiec.MarketData.Tests.Fixtures.PriceSources;

// Blocks the fetch until the test releases the gate, so a second trigger deterministically sees the first run in flight.
public sealed class GatedPriceSource(
    PriceSource source, TaskCompletionSource gate, PriceFetchResult<InstrumentQuote> result) : IPriceSource
{
    public PriceSource Source { get; } = source;

    public TimeSpan RequestDelay => TimeSpan.Zero;

    public int? MaxHistoryDays => null;

    public async Task<PriceFetchResult<InstrumentQuote>> FetchLatestAsync(
        IReadOnlyCollection<Instrument> instruments, CancellationToken cancellationToken)
    {
        await gate.Task.WaitAsync(cancellationToken);
        return result;
    }

    public Task<PriceFetchResult<InstrumentQuote>> FetchHistoryAsync(
        Instrument instrument, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        throw new NotSupportedException($"This {nameof(GatedPriceSource)} only supports {nameof(FetchLatestAsync)}.");
}
