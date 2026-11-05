using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;

namespace Skarbiec.MarketData.Tests.Fixtures.PriceSources;

// Source is read by the job outside its SafeFetch wrapper, so the throw is an unexpected failure inside the run.
public sealed class ThrowingPriceSource : IPriceSource
{
    public PriceSource Source => throw new InvalidOperationException("Unexpected failure inside the run.");

    public TimeSpan RequestDelay => TimeSpan.Zero;

    public int? MaxHistoryDays => null;

    public Task<PriceFetchResult<InstrumentQuote>> FetchLatestAsync(
        IReadOnlyCollection<Instrument> instruments, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<PriceFetchResult<InstrumentQuote>> FetchHistoryAsync(
        Instrument instrument, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
