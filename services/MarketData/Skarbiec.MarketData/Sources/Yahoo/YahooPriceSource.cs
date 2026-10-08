using System.Text.Json;
using System.Text.Json.Serialization;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Sources.Yahoo;

// Ticker holds the Yahoo symbol with its exchange suffix (CDR.WA), and Close is stored unconverted in QuoteCurrency.
public sealed class YahooPriceSource(IYahooApiClient client) : IPriceSource
{
    private const string NotFoundCode = "Not Found";

    public PriceSource Source => PriceSource.Yahoo;

    public TimeSpan RequestDelay => TimeSpan.Zero;

    public int? MaxHistoryDays => null;

    // Success while any instrument got a quote, Error only when one failed and none succeeded, NoData otherwise.
    public async Task<PriceFetchResult<InstrumentQuote>> FetchLatestAsync(
        IReadOnlyCollection<Instrument> instruments, CancellationToken cancellationToken)
    {
        var values = new List<InstrumentQuote>();
        string? lastErrorReason = null;

        foreach (var instrument in instruments)
        {
            string raw;
            try
            {
                raw = await client.GetLatestAsync(instrument.Ticker, cancellationToken);
            }
            catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
            {
                lastErrorReason = $"Yahoo request for {instrument.Ticker} failed: {ex.Message}";
                continue;
            }

            var parsed = Parse(raw, instrument.Id);
            if (parsed.Outcome == PriceFetchOutcome.Success)
            {
                values.Add(parsed.Values[^1]);
            }
            else if (parsed.Outcome == PriceFetchOutcome.Error)
            {
                lastErrorReason = parsed.ErrorReason;
            }
        }

        if (values.Count > 0)
        {
            return PriceFetchResult<InstrumentQuote>.Success(values);
        }

        return lastErrorReason is not null
            ? PriceFetchResult<InstrumentQuote>.Error(lastErrorReason)
            : PriceFetchResult<InstrumentQuote>.NoData();
    }

    public async Task<PriceFetchResult<InstrumentQuote>> FetchHistoryAsync(
        Instrument instrument, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        string raw;
        try
        {
            raw = await client.GetHistoryAsync(instrument.Ticker, from, to, cancellationToken);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            return PriceFetchResult<InstrumentQuote>.Error($"Yahoo history request for {instrument.Ticker} failed: {ex.Message}");
        }

        var parsed = Parse(raw, instrument.Id);
        if (parsed.Outcome != PriceFetchOutcome.Success)
        {
            return parsed;
        }

        var inRange = parsed.Values.Where(v => v.Date >= from && v.Date <= to).ToList();
        return inRange.Count > 0 ? PriceFetchResult<InstrumentQuote>.Success(inRange) : PriceFetchResult<InstrumentQuote>.NoData();
    }

    private static bool IsTransportFailure(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested;

    // {"chart":{"result":[{"meta":{...},"timestamp":[...],"indicators":{"quote":[{"close":[...]}]}}],"error":null}}; a day still trading has a null close.
    private static PriceFetchResult<InstrumentQuote> Parse(string raw, Guid instrumentId)
    {
        ChartEnvelopeDto? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<ChartEnvelopeDto>(raw);
        }
        catch (JsonException ex)
        {
            return PriceFetchResult<InstrumentQuote>.Error($"malformed Yahoo chart payload: {ex.Message}");
        }

        var chart = envelope?.Chart;
        if (chart is null)
        {
            return PriceFetchResult<InstrumentQuote>.Error("malformed Yahoo chart payload: missing chart");
        }

        if (chart.Error is not null)
        {
            return chart.Error.Code == NotFoundCode
                ? PriceFetchResult<InstrumentQuote>.NoData()
                : PriceFetchResult<InstrumentQuote>.Error($"Yahoo chart error {chart.Error.Code}: {chart.Error.Description}");
        }

        var result = chart.Result?.FirstOrDefault();
        if (result?.Timestamp is null || result.Timestamp.Count == 0)
        {
            return PriceFetchResult<InstrumentQuote>.NoData();
        }

        var closes = result.Indicators?.Quote?.FirstOrDefault()?.Close;
        if (closes is null || closes.Count != result.Timestamp.Count)
        {
            return PriceFetchResult<InstrumentQuote>.Error("malformed Yahoo chart payload: timestamps and closes differ in length");
        }

        var offset = ResolveOffset(result.Meta);
        var byDate = new SortedDictionary<DateOnly, decimal>();
        for (var i = 0; i < closes.Count; i++)
        {
            if (closes[i] is not { } close)
            {
                continue;
            }

            var instant = DateTimeOffset.FromUnixTimeSeconds(result.Timestamp[i]);
            byDate[DateOnly.FromDateTime(offset(instant).DateTime)] = close;
        }

        return byDate.Count > 0
            ? PriceFetchResult<InstrumentQuote>.Success(byDate.Select(kv => new InstrumentQuote(instrumentId, kv.Key, kv.Value)).ToList())
            : PriceFetchResult<InstrumentQuote>.NoData();
    }

    // The exchange's timezone keeps a close on its local trading day; gmtoffset is the fallback when the zone id is unknown to the host.
    private static Func<DateTimeOffset, DateTimeOffset> ResolveOffset(ChartMetaDto? meta)
    {
        if (meta?.ExchangeTimezoneName is { } zoneId && TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out var zone))
        {
            return instant => TimeZoneInfo.ConvertTime(instant, zone);
        }

        var fixedOffset = TimeSpan.FromSeconds(meta?.GmtOffset ?? 0);
        return instant => instant.ToOffset(fixedOffset);
    }

    private sealed record ChartEnvelopeDto([property: JsonPropertyName("chart")] ChartDto? Chart);

    private sealed record ChartDto(
        [property: JsonPropertyName("result")] List<ChartResultDto>? Result,
        [property: JsonPropertyName("error")] ChartErrorDto? Error);

    private sealed record ChartErrorDto(
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("description")] string? Description);

    private sealed record ChartResultDto(
        [property: JsonPropertyName("meta")] ChartMetaDto? Meta,
        [property: JsonPropertyName("timestamp")] List<long>? Timestamp,
        [property: JsonPropertyName("indicators")] IndicatorsDto? Indicators);

    private sealed record ChartMetaDto(
        [property: JsonPropertyName("exchangeTimezoneName")] string? ExchangeTimezoneName,
        [property: JsonPropertyName("gmtoffset")] int? GmtOffset);

    private sealed record IndicatorsDto([property: JsonPropertyName("quote")] List<QuoteDto>? Quote);

    private sealed record QuoteDto([property: JsonPropertyName("close")] List<decimal?>? Close);
}
