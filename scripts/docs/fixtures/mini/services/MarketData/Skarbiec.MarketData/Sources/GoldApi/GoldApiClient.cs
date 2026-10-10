namespace Skarbiec.MarketData.Sources.GoldApi;

public sealed class GoldApiClient(HttpClient httpClient) : IGoldApiClient
{
    public Task<decimal> GetPriceAsync(string symbol, CancellationToken cancellationToken) => Task.FromResult(0m);
}
