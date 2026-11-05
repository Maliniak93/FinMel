using Skarbiec.MarketData.Sources;

namespace Skarbiec.MarketData.Tests.Fixtures.PriceSources;

// RequestDelay is read by the job outside its SafeFetch wrapper, so the throw is an unexpected failure inside the run.
public sealed class ThrowingFxRateSource : IFxRateSource
{
    public TimeSpan RequestDelay => throw new InvalidOperationException("Unexpected failure inside the run.");

    public Task<PriceFetchResult<FxRateQuote>> FetchLatestAsync(
        IReadOnlyCollection<string> currencyCodes, CancellationToken cancellationToken) =>
        Task.FromResult(PriceFetchResult<FxRateQuote>.NoData());

    public Task<PriceFetchResult<FxRateQuote>> FetchHistoryAsync(
        string currencyCode, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        Task.FromResult(PriceFetchResult<FxRateQuote>.NoData());
}
