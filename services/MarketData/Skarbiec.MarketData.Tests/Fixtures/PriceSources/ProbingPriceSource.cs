using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;

namespace Skarbiec.MarketData.Tests.Fixtures.PriceSources;

// Runs a probe against the database at the moment the job fetches, then answers with the scripted history.
public sealed class ProbingPriceSource(
    Func<Task<bool>> probe,
    Func<Instrument, IReadOnlyList<InstrumentQuote>> history) : IPriceSource
{
    private readonly TaskCompletionSource<bool> _probed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public PriceSource Source => PriceSource.Yahoo;

    public TimeSpan RequestDelay => TimeSpan.Zero;

    public int? MaxHistoryDays => null;

    public Task<bool> ProbeResult => _probed.Task;

    public async Task<PriceFetchResult<InstrumentQuote>> FetchLatestAsync(
        IReadOnlyCollection<Instrument> instruments, CancellationToken cancellationToken)
    {
        _probed.TrySetResult(await probe());
        return PriceFetchResult<InstrumentQuote>.NoData();
    }

    public async Task<PriceFetchResult<InstrumentQuote>> FetchHistoryAsync(
        Instrument instrument, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        _probed.TrySetResult(await probe());
        return PriceFetchResult<InstrumentQuote>.Success(history(instrument));
    }
}
