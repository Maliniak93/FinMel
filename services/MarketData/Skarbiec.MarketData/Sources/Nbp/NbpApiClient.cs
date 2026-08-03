using System.Net;

namespace Skarbiec.MarketData.Sources.Nbp;

/// <summary>
/// Real HTTP implementation of <see cref="INbpApiClient"/> — a typed client resolved through
/// <see cref="IHttpClientFactory"/> (registered by <see cref="NbpSourceExtensions.AddNbpSources{TBuilder}"/>),
/// so ServiceDefaults' standard resilience handler (retry+jitter, circuit breaker, timeout) applies
/// automatically; no per-source wiring needed (see <see cref="IPriceSource"/>'s doc comment).
/// </summary>
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

    // 404 is NBP's "nothing published for that date/range" signal (verified live: weekends and
    // holidays 404 with a plain-text body, not JSON) — translated to null here so callers never see
    // it as a transport error. Any other non-success status (e.g. 400 range-too-long) still throws
    // via EnsureSuccessStatusCode, which the sources treat as PriceFetchOutcome.Error.
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
