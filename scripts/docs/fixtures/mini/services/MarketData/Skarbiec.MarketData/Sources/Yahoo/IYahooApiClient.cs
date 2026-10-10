namespace Skarbiec.MarketData.Sources.Yahoo;

public interface IYahooApiClient
{
    Task<string> GetChartAsync(string symbol, CancellationToken cancellationToken);
}
