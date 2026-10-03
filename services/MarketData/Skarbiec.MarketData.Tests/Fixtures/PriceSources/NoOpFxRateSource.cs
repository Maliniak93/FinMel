using Skarbiec.MarketData.Sources;

namespace Skarbiec.MarketData.Tests.Fixtures.PriceSources;

public sealed class NoOpFxRateSource : IFxRateSource
{
    public TimeSpan RequestDelay => TimeSpan.Zero;

    public Task<PriceFetchResult<FxRateQuote>> FetchLatestAsync(
        IReadOnlyCollection<string> currencyCodes, CancellationToken cancellationToken) =>
        Task.FromResult(PriceFetchResult<FxRateQuote>.NoData());

    public Task<PriceFetchResult<FxRateQuote>> FetchHistoryAsync(
        string currencyCode, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        Task.FromResult(PriceFetchResult<FxRateQuote>.NoData());
}
