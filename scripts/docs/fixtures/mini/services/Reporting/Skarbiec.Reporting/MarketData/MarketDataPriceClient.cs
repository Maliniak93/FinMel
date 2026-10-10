namespace Skarbiec.Reporting.MarketData;

public sealed class MarketDataPriceClient(HttpClient http)
{
    public async Task FetchAsync(CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync("/internal/prices/ghost-batch", new object(), cancellationToken);
    }
}
