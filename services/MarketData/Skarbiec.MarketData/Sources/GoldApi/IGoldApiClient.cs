namespace Skarbiec.MarketData.Sources.GoldApi;

// Returns null for a 404, gold-api.com's answer for an unknown symbol.
public interface IGoldApiClient
{
    Task<string?> GetPriceAsync(string symbol, CancellationToken cancellationToken);
}
