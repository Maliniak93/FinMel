using System.Text.Json;
using System.Text.Json.Serialization;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Sources.Nbp;

// NBP quotes 1 gram of gold in PLN, not a troy ounce, despite the XAU ticker.
public sealed class NbpPriceSource(INbpApiClient client) : IPriceSource
{
    private const string GoldTicker = "XAU";

    // Confirmed working; the true ceiling is somewhat higher but was never pinned down.
    private const int MaxRangeDays = 367;

    public PriceSource Source => PriceSource.Nbp;

    public TimeSpan RequestDelay => TimeSpan.Zero;

    public async Task<PriceFetchResult<InstrumentQuote>> FetchLatestAsync(
        IReadOnlyCollection<Instrument> instruments, CancellationToken cancellationToken)
    {
        string? raw;
        try
        {
            raw = await client.GetGoldPriceAsync(date: null, cancellationToken);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            return PriceFetchResult<InstrumentQuote>.Error($"NBP gold request failed: {ex.Message}");
        }

        return raw is null ? PriceFetchResult<InstrumentQuote>.NoData() : Parse(raw, instruments);
    }

    public async Task<PriceFetchResult<InstrumentQuote>> FetchHistoryAsync(
        Instrument instrument, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var values = new List<InstrumentQuote>();
        var sawAnyData = false;

        foreach (var (chunkFrom, chunkTo) in NbpDateRangeChunker.Chunk(from, to, MaxRangeDays))
        {
            string? raw;
            try
            {
                raw = await client.GetGoldPriceRangeAsync(chunkFrom, chunkTo, cancellationToken);
            }
            catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
            {
                return PriceFetchResult<InstrumentQuote>.Error($"NBP gold request failed: {ex.Message}");
            }

            if (raw is null)
            {
                continue;
            }

            sawAnyData = true;
            var chunkResult = Parse(raw, [instrument]);
            if (chunkResult.Outcome == PriceFetchOutcome.Error)
            {
                return chunkResult;
            }

            values.AddRange(chunkResult.Values);
        }

        return sawAnyData ? PriceFetchResult<InstrumentQuote>.Success(values) : PriceFetchResult<InstrumentQuote>.NoData();
    }

    private static bool IsTransportFailure(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested;

    private static PriceFetchResult<InstrumentQuote> Parse(string raw, IReadOnlyCollection<Instrument> instruments)
    {
        NbpGoldDto[]? entries;
        try
        {
            entries = JsonSerializer.Deserialize<NbpGoldDto[]>(raw);
        }
        catch (JsonException ex)
        {
            return PriceFetchResult<InstrumentQuote>.Error($"malformed gold payload: {ex.Message}");
        }

        if (entries is null or { Length: 0 })
        {
            return PriceFetchResult<InstrumentQuote>.NoData();
        }

        // Gold is NBP's only instrument; any other ticker is excluded, not an error.
        var goldInstrumentIds = instruments.Where(i => i.Ticker == GoldTicker).Select(i => i.Id);

        var values = goldInstrumentIds
            .SelectMany(id => entries.Select(e => new InstrumentQuote(id, DateOnly.Parse(e.Date), e.Cena)))
            .ToList();

        return PriceFetchResult<InstrumentQuote>.Success(values);
    }

    private sealed record NbpGoldDto(
        [property: JsonPropertyName("data")] string Date,
        [property: JsonPropertyName("cena")] decimal Cena);
}
