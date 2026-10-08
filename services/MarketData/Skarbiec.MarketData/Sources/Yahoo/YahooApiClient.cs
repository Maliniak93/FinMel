using System.Net;

namespace Skarbiec.MarketData.Sources.Yahoo;

public sealed class YahooApiClient(HttpClient httpClient) : IYahooApiClient
{
    public Task<string> GetLatestAsync(string ticker, CancellationToken cancellationToken) =>
        GetRawAsync($"v8/finance/chart/{Uri.EscapeDataString(ticker)}?range=5d&interval=1d", cancellationToken);

    // period2 is exclusive, so it points at the day after `to`.
    public Task<string> GetHistoryAsync(string ticker, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        GetRawAsync(
            $"v8/finance/chart/{Uri.EscapeDataString(ticker)}?period1={ToUnixSeconds(from)}&period2={ToUnixSeconds(to.AddDays(1))}&interval=1d",
            cancellationToken);

    public Task<string> SearchAsync(string query, CancellationToken cancellationToken) =>
        GetRawAsync($"v1/finance/search?q={Uri.EscapeDataString(query)}&quotesCount=20&newsCount=0", cancellationToken);

    private async Task<string> GetRawAsync(string requestUri, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(requestUri, cancellationToken);

        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static long ToUnixSeconds(DateOnly date) =>
        new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
}
