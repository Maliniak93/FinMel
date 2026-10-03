namespace Skarbiec.MarketData.Sources.Stooq;

// stooq.com currently gates requests behind a JavaScript challenge, which reaches the source as a malformed payload.
public sealed class StooqApiClient(HttpClient httpClient) : IStooqApiClient
{
    public async Task<string> GetLatestAsync(string ticker, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            $"q/l/?s={Uri.EscapeDataString(ticker)}&f=sd2t2ohlcv&h&e=csv", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<string> GetHistoryAsync(string ticker, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            $"q/d/l/?s={Uri.EscapeDataString(ticker)}&d1={Format(from)}&d2={Format(to)}&i=d", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static string Format(DateOnly date) => date.ToString("yyyyMMdd");
}
