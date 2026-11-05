using Skarbiec.MarketData.Sources;

namespace Skarbiec.MarketData.Tests.Fixtures.PriceSources;

public sealed class NeverAnsweringFxRateSource : IFxRateSource
{
    private readonly TaskCompletionSource _requested = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TimeSpan RequestDelay => TimeSpan.Zero;

    public Task Requested => _requested.Task;

    public async Task<PriceFetchResult<FxRateQuote>> FetchLatestAsync(
        IReadOnlyCollection<string> currencyCodes, CancellationToken cancellationToken)
    {
        _requested.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return PriceFetchResult<FxRateQuote>.NoData();
    }

    public async Task<PriceFetchResult<FxRateQuote>> FetchHistoryAsync(
        string currencyCode, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        _requested.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return PriceFetchResult<FxRateQuote>.NoData();
    }
}
