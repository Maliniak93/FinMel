namespace Skarbiec.MarketData.Sources.StooqApi;

public sealed class StooqApiClient(HttpClient httpClient) : IStooqApiClient
{
    public Task<decimal> GetPriceAsync(string symbol, CancellationToken cancellationToken) => Task.FromResult(0m);
}
