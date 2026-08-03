using System.Text.Json;
using System.Text.Json.Serialization;

namespace Skarbiec.MarketData.Sources.Nbp;

/// <summary>
/// <see cref="IFxRateSource"/> over NBP table A (E4 [M], ADR-008). Table A returns every published
/// currency in one call, so <see cref="FetchLatestAsync"/> fetches once and filters down to the
/// requested codes rather than one request per currency.
/// </summary>
public sealed class NbpFxRateSource(INbpApiClient client) : IFxRateSource
{
    private const string BaseCurrency = "PLN"; // ADR-008

    // Verified live against api.nbp.pl: a 94-day exchangerates/tables/A query 400s with "Limit of
    // 93 days has been exceeded"; 93 days succeeds.
    private const int MaxRangeDays = 93;

    public TimeSpan RequestDelay => TimeSpan.Zero;

    public async Task<PriceFetchResult<FxRateQuote>> FetchLatestAsync(
        IReadOnlyCollection<string> currencyCodes, CancellationToken cancellationToken)
    {
        string? raw;
        try
        {
            raw = await client.GetTableAAsync(date: null, cancellationToken);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            return PriceFetchResult<FxRateQuote>.Error($"NBP table A request failed: {ex.Message}");
        }

        return raw is null ? PriceFetchResult<FxRateQuote>.NoData() : Parse(raw, currencyCodes);
    }

    public async Task<PriceFetchResult<FxRateQuote>> FetchHistoryAsync(
        string currencyCode, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var values = new List<FxRateQuote>();
        var sawAnyData = false;

        foreach (var (chunkFrom, chunkTo) in NbpDateRangeChunker.Chunk(from, to, MaxRangeDays))
        {
            string? raw;
            try
            {
                raw = await client.GetTableARangeAsync(chunkFrom, chunkTo, cancellationToken);
            }
            catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
            {
                return PriceFetchResult<FxRateQuote>.Error($"NBP table A request failed: {ex.Message}");
            }

            if (raw is null)
            {
                continue;
            }

            sawAnyData = true;
            var chunkResult = Parse(raw, [currencyCode]);
            if (chunkResult.Outcome == PriceFetchOutcome.Error)
            {
                return chunkResult;
            }

            values.AddRange(chunkResult.Values);
        }

        return sawAnyData ? PriceFetchResult<FxRateQuote>.Success(values) : PriceFetchResult<FxRateQuote>.NoData();
    }

    private static bool IsTransportFailure(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested;

    private static PriceFetchResult<FxRateQuote> Parse(string raw, IReadOnlyCollection<string> currencyCodes)
    {
        NbpTableDto[]? tables;
        try
        {
            tables = JsonSerializer.Deserialize<NbpTableDto[]>(raw);
        }
        catch (JsonException ex)
        {
            return PriceFetchResult<FxRateQuote>.Error($"malformed table A payload: {ex.Message}");
        }

        if (tables is null or { Length: 0 })
        {
            return PriceFetchResult<FxRateQuote>.NoData();
        }

        var wanted = new HashSet<string>(currencyCodes, StringComparer.OrdinalIgnoreCase);
        var values = tables
            .SelectMany(t => t.Rates
                .Where(r => wanted.Contains(r.Code))
                .Select(r => new FxRateQuote(
                    r.Code.ToUpperInvariant() + BaseCurrency,
                    DateOnly.Parse(t.EffectiveDate),
                    r.Mid)))
            .ToList();

        return PriceFetchResult<FxRateQuote>.Success(values);
    }

    private sealed record NbpTableDto(
        [property: JsonPropertyName("effectiveDate")] string EffectiveDate,
        [property: JsonPropertyName("rates")] IReadOnlyList<NbpRateDto> Rates);

    private sealed record NbpRateDto(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("mid")] decimal Mid);
}
