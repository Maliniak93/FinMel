namespace Skarbiec.MarketData.Sources.CoinGecko;

public interface ICoinGeckoApiClient
{
    Task<string> GetLatestAsync(IReadOnlyCollection<string> coinGeckoIds, CancellationToken cancellationToken);

    // Per coin: the market-chart endpoint takes a single id.
    Task<string> GetHistoryAsync(string coinGeckoId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
