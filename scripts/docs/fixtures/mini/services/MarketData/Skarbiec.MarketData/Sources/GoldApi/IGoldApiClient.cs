namespace Skarbiec.MarketData.Sources.GoldApi;

public interface IGoldApiClient
{
    Task<decimal> GetPriceAsync(string symbol, CancellationToken cancellationToken);
}
