using System.Text.Json;
using System.Text.Json.Serialization;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Sources.GoldApi;

// gold-api.com quotes USD per troy ounce; quotes are stored per gram of fine metal. Its free tier has no history, only the latest spot.
public sealed class GoldApiPriceSource(IGoldApiClient client, ILogger<GoldApiPriceSource> logger) : IPriceSource
{
    public const decimal GramsPerTroyOunce = 31.1034768m;

    public PriceSource Source => PriceSource.GoldApi;

    public TimeSpan RequestDelay => TimeSpan.Zero;

    // Success while any instrument got a quote, Error only when every one failed, NoData when none had anything.
    public async Task<PriceFetchResult<InstrumentQuote>> FetchLatestAsync(
        IReadOnlyCollection<Instrument> instruments, CancellationToken cancellationToken)
    {
        var values = new List<InstrumentQuote>();
        string? firstError = null;

        foreach (var instrument in instruments)
        {
            var result = await FetchOneAsync(instrument, cancellationToken);
            if (result.Outcome == PriceFetchOutcome.Error)
            {
                logger.LogWarning("gold-api.com price for {Ticker} failed: {Reason}", instrument.Ticker, result.ErrorReason);
                firstError ??= result.ErrorReason;
            }

            values.AddRange(result.Values);
        }

        if (values.Count > 0)
        {
            return PriceFetchResult<InstrumentQuote>.Success(values);
        }

        return firstError is null ? PriceFetchResult<InstrumentQuote>.NoData() : PriceFetchResult<InstrumentQuote>.Error(firstError);
    }

    public async Task<PriceFetchResult<InstrumentQuote>> FetchHistoryAsync(
        Instrument instrument, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var latest = await FetchOneAsync(instrument, cancellationToken);
        if (latest.Outcome != PriceFetchOutcome.Success)
        {
            return latest;
        }

        var inRange = latest.Values.Where(q => q.Date >= from && q.Date <= to).ToList();
        return inRange.Count > 0 ? PriceFetchResult<InstrumentQuote>.Success(inRange) : PriceFetchResult<InstrumentQuote>.NoData();
    }

    private async Task<PriceFetchResult<InstrumentQuote>> FetchOneAsync(Instrument instrument, CancellationToken cancellationToken)
    {
        string? raw;
        try
        {
            raw = await client.GetPriceAsync(instrument.Ticker, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return PriceFetchResult<InstrumentQuote>.Error($"gold-api.com request for {instrument.Ticker} failed: {ex.Message}");
        }

        return raw is null ? PriceFetchResult<InstrumentQuote>.NoData() : Parse(raw, instrument);
    }

    // {"currency":"USD","price":<per troy oz>,"symbol":"XAG","updatedAt":"<ISO UTC>"}
    private static PriceFetchResult<InstrumentQuote> Parse(string raw, Instrument instrument)
    {
        PriceDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<PriceDto>(raw);
        }
        catch (JsonException ex)
        {
            return PriceFetchResult<InstrumentQuote>.Error($"malformed gold-api.com payload for {instrument.Ticker}: {ex.Message}");
        }

        if (dto?.Price is not { } pricePerOunce || dto.UpdatedAt is not { } updatedAt)
        {
            return PriceFetchResult<InstrumentQuote>.NoData();
        }

        var date = DateOnly.FromDateTime(updatedAt.UtcDateTime);
        var pricePerGram = Math.Round(pricePerOunce / GramsPerTroyOunce, 8);
        return PriceFetchResult<InstrumentQuote>.Success([new InstrumentQuote(instrument.Id, date, pricePerGram)]);
    }

    private sealed record PriceDto(
        [property: JsonPropertyName("price")] decimal? Price,
        [property: JsonPropertyName("updatedAt")] DateTimeOffset? UpdatedAt);
}
