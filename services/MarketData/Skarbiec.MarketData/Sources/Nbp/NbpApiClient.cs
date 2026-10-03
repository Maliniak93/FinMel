using System.Net;

namespace Skarbiec.MarketData.Sources.Nbp;

public sealed class NbpApiClient(HttpClient httpClient) : INbpApiClient
{
    public Task<string?> GetTableAAsync(DateOnly? date, CancellationToken cancellationToken) =>
        GetRawAsync(date is null
            ? "exchangerates/tables/A/?format=json"
            : $"exchangerates/tables/A/{Format(date.Value)}/?format=json", cancellationToken);

    public Task<string?> GetTableARangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        GetRawAsync($"exchangerates/tables/A/{Format(from)}/{Format(to)}/?format=json", cancellationToken);

    public Task<string?> GetGoldPriceAsync(DateOnly? date, CancellationToken cancellationToken) =>
        GetRawAsync(date is null
            ? "cenyzlota/?format=json"
            : $"cenyzlota/{Format(date.Value)}/?format=json", cancellationToken);

    public Task<string?> GetGoldPriceRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        GetRawAsync($"cenyzlota/{Format(from)}/{Format(to)}/?format=json", cancellationToken);

    // NBP answers 404 with a plain-text body for a day with no data; any other failure still throws.
    private async Task<string?> GetRawAsync(string requestUri, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(requestUri, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static string Format(DateOnly date) => date.ToString("yyyy-MM-dd");
}
