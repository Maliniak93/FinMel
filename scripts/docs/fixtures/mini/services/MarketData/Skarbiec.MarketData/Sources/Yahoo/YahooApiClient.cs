namespace Skarbiec.MarketData.Sources.Yahoo;

public sealed class YahooApiClient(HttpClient httpClient) : IYahooApiClient
{
    public Task<string> GetChartAsync(string symbol, CancellationToken cancellationToken) =>
        httpClient.GetStringAsync($"v8/finance/chart/{symbol}", cancellationToken);
}
