using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Sources;

public interface IPriceSource
{
    // Callers pass only instruments of this source; a source does not filter itself.
    PriceSource Source { get; }

    // Zero for a source without a rate limit; a caller asks for batching by passing more instruments.
    TimeSpan RequestDelay { get; }

    // A day with no trading returns NoData, not an error.
    Task<PriceFetchResult<InstrumentQuote>> FetchLatestAsync(
        IReadOnlyCollection<Instrument> instruments, CancellationToken cancellationToken);

    Task<PriceFetchResult<InstrumentQuote>> FetchHistoryAsync(
        Instrument instrument, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
