namespace Skarbiec.MarketData.Sources.Stooq;

/// <summary>
/// Real HTTP implementation of <see cref="IStooqApiClient"/> — a typed client resolved through
/// <see cref="IHttpClientFactory"/> (registered by <see cref="StooqSourceExtensions.AddStooqSource{TBuilder}"/>),
/// so ServiceDefaults' standard resilience handler (retry+jitter, circuit breaker, timeout) applies
/// automatically; no per-source wiring needed (see <see cref="IPriceSource"/>'s doc comment).
/// </summary>
/// <remarks>
/// Live check against stooq.com on 2026-08-03: the site currently gates every request — including
/// these long-documented CSV endpoints — behind a JavaScript proof-of-work challenge page (some
/// requests 404 outright instead). A plain server-side <see cref="HttpClient"/> cannot solve that
/// challenge, so today this client cannot reach real data in production; it still parses correctly
/// against recorded fixtures (T2.4 AC), and a non-CSV challenge/error page reaching
/// <see cref="StooqPriceSource"/> is handled as a malformed payload (<c>PriceFetchOutcome.Error</c>),
/// not an unhandled exception. This is the roadmap's "Stooq/CoinGecko changes its format" risk
/// materializing as a harder access block — worth revisiting before T2.6 wires the sync job.
/// </remarks>
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
