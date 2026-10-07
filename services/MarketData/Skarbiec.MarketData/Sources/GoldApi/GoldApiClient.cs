using System.Net;

namespace Skarbiec.MarketData.Sources.GoldApi;

public sealed class GoldApiClient(HttpClient httpClient) : IGoldApiClient
{
    public async Task<string?> GetPriceAsync(string symbol, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"price/{Uri.EscapeDataString(symbol)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}
