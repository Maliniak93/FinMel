using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Tests.Fixtures.PriceSources;

namespace Skarbiec.MarketData.Tests;

public sealed class PriceSourceAbstractionTests
{
    private static readonly Instrument SampleInstrument = new()
    {
        Id = Guid.NewGuid(),
        Ticker = "AAPL.US",
        Name = "Apple Inc.",
        Source = PriceSource.Stooq,
        QuoteCurrency = "USD",
        AssetClass = AssetClass.Stock,
    };

    [Fact]
    public async Task FetchLatestAsync_HappyPath_ReturnsSuccessWithParsedQuote()
    {
        var source = new FixturePriceSource(PriceSource.Stooq, RecordedResponse.Read("happy-path.json"));

        var result = await source.FetchLatestAsync([SampleInstrument], TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.Success, result.Outcome);
        var quote = Assert.Single(result.Values);
        Assert.Equal(SampleInstrument.Id, quote.InstrumentId);
        Assert.Equal(new DateOnly(2026, 8, 3), quote.Date);
        Assert.Equal(187.45m, quote.Close);
    }

    [Fact]
    public async Task FetchLatestAsync_NonTradingDay_ReturnsNoData_NotError()
    {
        var source = new FixturePriceSource(PriceSource.Stooq, RecordedResponse.Read("empty-day.json"));

        var result = await source.FetchLatestAsync([SampleInstrument], TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.NoData, result.Outcome);
        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task FetchLatestAsync_MalformedPayload_ReturnsErrorResult_DoesNotThrow()
    {
        var source = new FixturePriceSource(PriceSource.Stooq, RecordedResponse.Read("malformed.json"));

        var result = await source.FetchLatestAsync([SampleInstrument], TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.Error, result.Outcome);
        Assert.NotNull(result.ErrorReason);
        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task JobLikeConsumer_DependingOnlyOnIPriceSource_WorksAcrossImplementations()
    {
        IPriceSource[] sources =
        [
            new FixturePriceSource(PriceSource.Stooq, RecordedResponse.Read("happy-path.json")),
            new AlwaysNoDataPriceSource(),
        ];

        foreach (var source in sources)
        {
            var result = await FetchThroughAbstractionOnly(source, [SampleInstrument]);

            Assert.True(result.Outcome is PriceFetchOutcome.Success or PriceFetchOutcome.NoData);
        }
    }

    // Knows only IPriceSource, standing in for a caller such as PriceSyncJob.
    private static Task<PriceFetchResult<InstrumentQuote>> FetchThroughAbstractionOnly(
        IPriceSource source, IReadOnlyCollection<Instrument> instruments) =>
        source.FetchLatestAsync(instruments, TestContext.Current.CancellationToken);

    private sealed class AlwaysNoDataPriceSource : IPriceSource
    {
        public PriceSource Source => PriceSource.CoinGecko;

        public TimeSpan RequestDelay => TimeSpan.FromSeconds(2);

        public Task<PriceFetchResult<InstrumentQuote>> FetchLatestAsync(
            IReadOnlyCollection<Instrument> instruments, CancellationToken cancellationToken) =>
            Task.FromResult(PriceFetchResult<InstrumentQuote>.NoData());

        public Task<PriceFetchResult<InstrumentQuote>> FetchHistoryAsync(
            Instrument instrument, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
            Task.FromResult(PriceFetchResult<InstrumentQuote>.NoData());
    }
}
