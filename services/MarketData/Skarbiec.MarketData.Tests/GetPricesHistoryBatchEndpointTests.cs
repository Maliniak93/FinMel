using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Features.GetPricesHistoryBatch;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing.Containers;
using static Skarbiec.MarketData.Tests.Fixtures.MarketDataApi;

namespace Skarbiec.MarketData.Tests;

// An anonymous /internal endpoint, so every fact calls it with no token, as Reporting does.
public sealed class GetPricesHistoryBatchEndpointTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    [Fact]
    public async Task Post_ReturnsQuotesInRangePlusLastBeforeFrom()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        var instrumentId = await seedDb.SeedInstrumentAsync("AAPL.US", "Apple Inc.", PriceSource.Yahoo, "USD", cancellationToken);
        await seedDb.SeedQuoteAsync(instrumentId, new DateOnly(2026, 1, 2), 100m, cancellationToken);
        await seedDb.SeedQuoteAsync(instrumentId, new DateOnly(2026, 1, 5), 105m, cancellationToken);
        await seedDb.SeedQuoteAsync(instrumentId, new DateOnly(2026, 1, 6), 106m, cancellationToken);
        await seedDb.SeedQuoteAsync(instrumentId, new DateOnly(2026, 1, 12), 112m, cancellationToken);

        using var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            InternalPricesHistoryBatchUri,
            new { InstrumentIds = new[] { instrumentId }, From = new DateOnly(2026, 1, 5), To = new DateOnly(2026, 1, 10) },
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PricesHistoryBatchResponse>(cancellationToken);
        var series = Assert.Single(body!.Series);
        Assert.Equal(instrumentId, series.InstrumentId);
        Assert.Equal("USD", series.QuoteCurrency);
        Assert.Equal(
            new[] { new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 6) },
            series.Quotes.Select(q => q.Date));
        Assert.Equal(new[] { 100m, 105m, 106m }, series.Quotes.Select(q => q.Close));
    }

    [Fact]
    public async Task Metal_IsServedPerGram_WeekendCarriesLastClose()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        var goldId = await seedDb.SeedInstrumentAsync("GC=F", "Gold (1 g)", PriceSource.Yahoo, "USD", cancellationToken, AssetClass.PreciousMetal);
        var stockId = await seedDb.SeedInstrumentAsync("AAPL.US", "Apple Inc.", PriceSource.Yahoo, "USD", cancellationToken);
        await seedDb.SeedQuoteAsync(goldId, new DateOnly(2026, 1, 8), 6220.69536m, cancellationToken);
        await seedDb.SeedQuoteAsync(goldId, new DateOnly(2026, 1, 9), 9331.04304m, cancellationToken);
        await seedDb.SeedQuoteAsync(stockId, new DateOnly(2026, 1, 9), 160m, cancellationToken);

        using var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            InternalPricesHistoryBatchUri,
            new { InstrumentIds = new[] { goldId, stockId }, From = new DateOnly(2026, 1, 8), To = new DateOnly(2026, 1, 11) },
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PricesHistoryBatchResponse>(cancellationToken);
        var gold = Assert.Single(body!.Series, s => s.InstrumentId == goldId);
        var stock = Assert.Single(body.Series, s => s.InstrumentId == stockId);
        Assert.Equal(new[] { 200m, 300m }, gold.Quotes.Select(q => q.Close));
        Assert.Equal(new DateOnly(2026, 1, 9), gold.Quotes[^1].Date);
        Assert.Equal(160m, Assert.Single(stock.Quotes).Close);
    }

    [Fact]
    public async Task Post_InstrumentWithNoQuote_IsAbsent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        var noQuoteInstrumentId = await seedDb.SeedInstrumentAsync("MSFT.US", "Microsoft Corp.", PriceSource.Yahoo, "USD", cancellationToken);
        // Its only quote is dated after To, so it has no quote on or before To.
        var laterOnlyInstrumentId = await seedDb.SeedInstrumentAsync("NVDA.US", "NVIDIA Corp.", PriceSource.Yahoo, "USD", cancellationToken);
        await seedDb.SeedQuoteAsync(laterOnlyInstrumentId, new DateOnly(2026, 1, 12), 112m, cancellationToken);

        using var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            InternalPricesHistoryBatchUri,
            new { InstrumentIds = new[] { noQuoteInstrumentId, laterOnlyInstrumentId }, From = new DateOnly(2026, 1, 5), To = new DateOnly(2026, 1, 10) },
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PricesHistoryBatchResponse>(cancellationToken);
        Assert.Empty(body!.Series);
    }

    [Fact]
    public async Task Post_InvalidRequest_Returns400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var from = new DateOnly(2026, 1, 5);
        var to = new DateOnly(2026, 1, 10);
        using var client = Factory.CreateClient();

        var fromAfterTo = await client.PostAsJsonAsync(
            InternalPricesHistoryBatchUri,
            new { InstrumentIds = new[] { Guid.NewGuid() }, From = to, To = from },
            cancellationToken);
        var emptyIds = await client.PostAsJsonAsync(
            InternalPricesHistoryBatchUri,
            new { InstrumentIds = Array.Empty<Guid>(), From = from, To = to },
            cancellationToken);
        var tooManyIds = await client.PostAsJsonAsync(
            InternalPricesHistoryBatchUri,
            new { InstrumentIds = Enumerable.Range(0, 1001).Select(_ => Guid.NewGuid()).ToArray(), From = from, To = to },
            cancellationToken);

        Assert.Multiple(
            () => Assert.Equal(HttpStatusCode.BadRequest, fromAfterTo.StatusCode),
            () => Assert.Equal(HttpStatusCode.BadRequest, emptyIds.StatusCode),
            () => Assert.Equal(HttpStatusCode.BadRequest, tooManyIds.StatusCode));
    }
}
