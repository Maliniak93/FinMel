using System.Globalization;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Sources.Stooq;

/// <summary>
/// <see cref="IPriceSource"/> over stooq.com's CSV endpoints — EOD quotes for GPW stocks, ETFs and
/// metals (E4 [M]). <see cref="Instrument.Ticker"/> stores Stooq's own ticker convention directly
/// (e.g. <c>AAPL.US</c>, <c>CDR.PL</c> — Warsaw Stock Exchange listings use Stooq's own
/// <c>.PL</c> suffix, not the <c>.WA</c> suffix some other providers/brokers use); no ticker
/// translation happens here, and no currency inference either — <see cref="Instrument.QuoteCurrency"/>
/// is trusted as-is (set at instrument creation, T2.1/T2.8) and the parsed <c>Close</c> is stored
/// unconverted, exactly as Stooq returns it (T2.4 AC: conversion happens at valuation, not ingestion).
/// </summary>
public sealed class StooqPriceSource(IStooqApiClient client) : IPriceSource
{
    private const string NoDataMarker = "N/D";
    private const string NoDataLine = "No data";

    public PriceSource Source => PriceSource.Stooq;

    public TimeSpan RequestDelay => TimeSpan.Zero;

    /// <summary>
    /// Fetches each instrument's latest quote as its own request (T2.4 scope: "per-ticker fetch").
    /// A single bad ticker — transport failure, malformed row — is excluded rather than failing the
    /// whole call: the result is <c>Success</c> as long as at least one instrument produced a quote,
    /// <c>Error</c> only when every instrument failed outright, and <c>NoData</c> when every
    /// instrument simply had nothing to report (e.g. a market-wide non-trading day).
    /// </summary>
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

    // Expected shape: a header line ("Symbol,Date,Time,Open,High,Low,Close,Volume") plus exactly one
    // data row, since each ticker is its own request. An unknown ticker or a closed market comes back
    // as a row with "N/D" fields rather than a missing row.
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

    // Expected shape: a header line ("Date,Open,High,Low,Close,Volume") plus one row per trading day
    // in range. An invalid ticker or an empty range comes back as the literal body "No data" instead
    // of a CSV — Stooq's long-documented no-data signal for this endpoint.
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

    // Culture-invariant: Stooq always renders dates/decimals in an invariant format, but the host's
    // culture must never leak in (T2.4 scope).
    private static bool TryParseDate(string field, out DateOnly date) =>
        DateOnly.TryParseExact(field, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static bool TryParseClose(string field, out decimal close) =>
        decimal.TryParse(field, NumberStyles.Number, CultureInfo.InvariantCulture, out close);
}
