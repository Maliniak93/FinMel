using Skarbiec.MarketData.Sources;

namespace Skarbiec.MarketData.Tests.Fixtures.PriceSources;

/// <summary>Always reports "no data" — stands in for <see cref="IFxRateSource"/> in scheduling tests
/// that run a real, DI-wired <see cref="Skarbiec.MarketData.Sources.PriceSyncJob"/> but don't care
/// about FX rates (seeded instruments are PLN-quoted, so the job never actually calls this).</summary>
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
