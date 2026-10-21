namespace Skarbiec.MarketData.Sources.StooqApi;

public interface IStooqApiClient
{
    Task<decimal> GetPriceAsync(string symbol, CancellationToken cancellationToken);
}
