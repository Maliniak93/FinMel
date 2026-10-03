using System.Globalization;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Sources.Stooq;

// Ticker holds Stooq's own convention (CDR.PL, not CDR.WA), and Close is stored unconverted in QuoteCurrency.
public sealed class StooqPriceSource(IStooqApiClient client) : IPriceSource
{
    private const string NoDataMarker = "N/D";
    private const string NoDataLine = "No data";

    public PriceSource Source => PriceSource.Stooq;

    public TimeSpan RequestDelay => TimeSpan.Zero;

    // Success while any instrument got a quote, Error only when every one failed, NoData when none had anything.
    public async Task<PriceFetchResult<InstrumentQuote>> FetchLatestAsync(
        IReadOnlyCollection<Instrument> instruments, CancellationToken cancellationToken)
    {
        var values = new List<InstrumentQuote>();
        var anyFailed = false;
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
                anyFailed = true;
                lastErrorReason = $"Stooq request for {instrument.Ticker} failed: {ex.Message}";
                continue;
            }

            var parsed = ParseLatest(raw, instrument.Id);
            if (parsed.Outcome == PriceFetchOutcome.Success)
            {
                values.AddRange(parsed.Values);
            }
            else if (parsed.Outcome == PriceFetchOutcome.Error)
            {
                anyFailed = true;
                lastErrorReason = parsed.ErrorReason;
            }
        }

        if (values.Count > 0)
        {
            return PriceFetchResult<InstrumentQuote>.Success(values);
        }

        return anyFailed
            ? PriceFetchResult<InstrumentQuote>.Error(lastErrorReason!)
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
            return PriceFetchResult<InstrumentQuote>.Error($"Stooq history request for {instrument.Ticker} failed: {ex.Message}");
        }

        return ParseHistory(raw, instrument.Id);
    }

    private static bool IsTransportFailure(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested;

    // A header plus one row per request; an unknown ticker or a closed market comes back as an N/D row.
    private static PriceFetchResult<InstrumentQuote> ParseLatest(string raw, Guid instrumentId)
    {
        var lines = SplitLines(raw);
        if (lines.Count < 2)
        {
            return PriceFetchResult<InstrumentQuote>.Error("malformed Stooq latest-quote payload: missing data row");
        }

        var fields = lines[1].Split(',');
        if (fields.Length != 8)
        {
            return PriceFetchResult<InstrumentQuote>.Error(
                $"malformed Stooq latest-quote payload: expected 8 columns, got {fields.Length}");
        }

        var dateField = fields[1];
        var closeField = fields[6];
        if (dateField == NoDataMarker || closeField == NoDataMarker)
        {
            return PriceFetchResult<InstrumentQuote>.NoData();
        }

        if (!TryParseDate(dateField, out var date) || !TryParseClose(closeField, out var close))
        {
            return PriceFetchResult<InstrumentQuote>.Error("malformed Stooq latest-quote payload: unparsable date/close");
        }

        return PriceFetchResult<InstrumentQuote>.Success([new InstrumentQuote(instrumentId, date, close)]);
    }

    // A header plus one row per trading day; an invalid ticker or an empty range comes back as the literal body "No data".
    private static PriceFetchResult<InstrumentQuote> ParseHistory(string raw, Guid instrumentId)
    {
        if (raw.Trim().Equals(NoDataLine, StringComparison.OrdinalIgnoreCase))
        {
            return PriceFetchResult<InstrumentQuote>.NoData();
        }

        var lines = SplitLines(raw);
        if (lines.Count == 0 || lines[0].Split(',').Length != 6)
        {
            return PriceFetchResult<InstrumentQuote>.Error("malformed Stooq history payload: unrecognized header");
        }

        var values = new List<InstrumentQuote>();
        for (var i = 1; i < lines.Count; i++)
        {
            var fields = lines[i].Split(',');
            if (fields.Length != 6)
            {
                return PriceFetchResult<InstrumentQuote>.Error(
                    $"malformed Stooq history payload: expected 6 columns, got {fields.Length}");
            }

            if (!TryParseDate(fields[0], out var date) || !TryParseClose(fields[4], out var close))
            {
                return PriceFetchResult<InstrumentQuote>.Error("malformed Stooq history payload: unparsable date/close");
            }

            values.Add(new InstrumentQuote(instrumentId, date, close));
        }

        return values.Count > 0 ? PriceFetchResult<InstrumentQuote>.Success(values) : PriceFetchResult<InstrumentQuote>.NoData();
    }

    private static List<string> SplitLines(string raw) =>
        raw.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();

    // Culture-invariant, so the host's culture never leaks into parsing.
    private static bool TryParseDate(string field, out DateOnly date) =>
        DateOnly.TryParseExact(field, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static bool TryParseClose(string field, out decimal close) =>
        decimal.TryParse(field, NumberStyles.Number, CultureInfo.InvariantCulture, out close);
}
