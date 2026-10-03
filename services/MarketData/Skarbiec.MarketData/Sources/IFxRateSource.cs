namespace Skarbiec.MarketData.Sources;

public interface IFxRateSource
{
    TimeSpan RequestDelay { get; }

    // A day with no trading returns NoData, not an error.
    Task<PriceFetchResult<FxRateQuote>> FetchLatestAsync(
        IReadOnlyCollection<string> currencyCodes, CancellationToken cancellationToken);

    Task<PriceFetchResult<FxRateQuote>> FetchHistoryAsync(
        string currencyCode, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
