using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Sources.Stooq;
using Skarbiec.MarketData.Tests.Fixtures.PriceSources;

namespace Skarbiec.MarketData.Tests;

/// <summary>
/// Fixture-based tests for the real Stooq source (T2.4 AC: "normal CSV, empty/N/D, malformed",
/// "historical range fetch returns a correctly parsed series", "quote currency respected"), via
/// <see cref="FakeStooqApiClient"/> — same test-kit pattern as T2.3's <c>NbpSourceTests</c>.
/// <c>stooq-malformed.csv</c> is an actual response recorded live from stooq.com on 2026-08-03 (a
/// 404 error page, not the expected CSV) — see <see cref="StooqApiClient"/>'s doc comment.
/// </summary>
public sealed class StooqSourceTests
{
    private static readonly Instrument UsdStock = new()
    {
        Id = Guid.NewGuid(),
        Ticker = "AAPL.US",
        Name = "Apple Inc.",
        Source = PriceSource.Stooq,
        QuoteCurrency = "USD",
        AssetClass = AssetClass.Stock,
    };

    private static readonly Instrument PlnStock = new()
    {
        Id = Guid.NewGuid(),
        Ticker = "CDR.PL",
        Name = "CD Projekt",
        Source = PriceSource.Stooq,
        QuoteCurrency = "PLN",
        AssetClass = AssetClass.Stock,
    };

    [Fact]
    public async Task FetchLatestAsync_HappyPath_ParsesCsv()
    {
        var client = new FakeStooqApiClient()
            .WithResponse(UsdStock.Ticker, RecordedResponse.Read("stooq-latest-happy-path.csv"));
        var source = new StooqPriceSource(client);

        var result = await source.FetchLatestAsync([UsdStock], TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.Success, result.Outcome);
        var quote = Assert.Single(result.Values);
        Assert.Equal(UsdStock.Id, quote.InstrumentId);
        Assert.Equal(new DateOnly(2026, 8, 3), quote.Date);
        Assert.Equal(232.47m, quote.Close);
    }

    [Fact]
    public async Task FetchLatestAsync_NonTradingDay_ReturnsNoData_NotError()
    {
        var client = new FakeStooqApiClient()
            .WithResponse(UsdStock.Ticker, RecordedResponse.Read("stooq-latest-no-data.csv"));
        var source = new StooqPriceSource(client);

        var result = await source.FetchLatestAsync([UsdStock], TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.NoData, result.Outcome);
        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task FetchLatestAsync_MalformedPayload_ReturnsErrorResult_DoesNotThrow()
    {
        var client = new FakeStooqApiClient()
            .WithResponse(UsdStock.Ticker, RecordedResponse.Read("stooq-malformed.csv"));
        var source = new StooqPriceSource(client);

        var result = await source.FetchLatestAsync([UsdStock], TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.Error, result.Outcome);
        Assert.NotNull(result.ErrorReason);
        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task FetchLatestAsync_TransportFailure_ReturnsErrorResult_DoesNotThrow()
    {
        var client = new FakeStooqApiClient()
            .ThrowingFor(UsdStock.Ticker, new HttpRequestException("simulated network failure"));
        var source = new StooqPriceSource(client);

        var result = await source.FetchLatestAsync([UsdStock], TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.Error, result.Outcome);
        Assert.NotNull(result.ErrorReason);
    }

    [Fact]
    public async Task FetchLatestAsync_OneBadTickerAmongMany_DoesNotFailBatch()
    {
        var client = new FakeStooqApiClient()
            .WithResponse(UsdStock.Ticker, RecordedResponse.Read("stooq-latest-happy-path.csv"))
            .ThrowingFor(PlnStock.Ticker, new HttpRequestException("simulated network failure"));
        var source = new StooqPriceSource(client);

        var result = await source.FetchLatestAsync([UsdStock, PlnStock], TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.Success, result.Outcome);
        var quote = Assert.Single(result.Values);
        Assert.Equal(UsdStock.Id, quote.InstrumentId);
    }

    [Fact]
    public async Task FetchHistoryAsync_HappyPath_ReturnsCorrectlyParsedSeries()
    {
        var client = new FakeStooqApiClient()
            .WithResponse(PlnStock.Ticker, RecordedResponse.Read("stooq-history-happy-path.csv"));
        var source = new StooqPriceSource(client);
        var from = new DateOnly(2026, 7, 30);
        var to = new DateOnly(2026, 8, 3);

        var result = await source.FetchHistoryAsync(PlnStock, from, to, TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.Success, result.Outcome);
        Assert.Equal(3, result.Values.Count);
        Assert.All(result.Values, v => Assert.Equal(PlnStock.Id, v.InstrumentId));
        Assert.Equal(new DateOnly(2026, 7, 30), result.Values[0].Date);
        Assert.Equal(188.10m, result.Values[0].Close);
        Assert.Equal(new DateOnly(2026, 8, 3), result.Values[^1].Date);
        Assert.Equal(190.40m, result.Values[^1].Close);
        Assert.Equal(1, client.HistoryRequestCount);
    }

    [Fact]
    public async Task FetchHistoryAsync_NoData_ReturnsNoData_NotError()
    {
        var client = new FakeStooqApiClient()
            .WithResponse(UsdStock.Ticker, RecordedResponse.Read("stooq-history-no-data.csv"));
        var source = new StooqPriceSource(client);

        var result = await source.FetchHistoryAsync(
            UsdStock, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.NoData, result.Outcome);
        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task FetchHistoryAsync_MalformedPayload_ReturnsErrorResult_DoesNotThrow()
    {
        var client = new FakeStooqApiClient()
            .WithResponse(UsdStock.Ticker, RecordedResponse.Read("stooq-malformed.csv"));
        var source = new StooqPriceSource(client);

        var result = await source.FetchHistoryAsync(
            UsdStock, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.Error, result.Outcome);
        Assert.NotNull(result.ErrorReason);
    }

    [Fact]
    public async Task FetchLatestAsync_QuoteCurrencyRespected_ClosePassesThroughUnconverted()
    {
        var client = new FakeStooqApiClient()
            .WithResponse(UsdStock.Ticker, RecordedResponse.Read("stooq-latest-happy-path.csv"))
            .WithResponse(PlnStock.Ticker, RecordedResponse.Read("stooq-latest-pln-happy-path.csv"));
        var source = new StooqPriceSource(client);

        var result = await source.FetchLatestAsync([UsdStock, PlnStock], TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.Success, result.Outcome);
        Assert.Equal(2, result.Values.Count);
        // Both currencies' Close values land exactly as Stooq reported them — no FX applied at
        // ingestion regardless of Instrument.QuoteCurrency (T2.4 AC); conversion is a valuation-time
        // concern (ADR-008), not this source's.
        var usdQuote = Assert.Single(result.Values, v => v.InstrumentId == UsdStock.Id);
        Assert.Equal(232.47m, usdQuote.Close);
        var plnQuote = Assert.Single(result.Values, v => v.InstrumentId == PlnStock.Id);
        Assert.Equal(187.45m, plnQuote.Close);
    }
}
