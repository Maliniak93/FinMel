using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing.Containers;
using static Skarbiec.MarketData.Tests.Fixtures.MarketDataApi;

namespace Skarbiec.MarketData.Tests;

// An anonymous /internal endpoint, so every fact calls it with no token, as Portfolio does.
public sealed class GetInstrumentsBatchEndpointTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    [Fact]
    public async Task Post_KnownAndUnknownIds_ReturnsKnownWithLatestCloseAndSkipsUnknown()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Guid vwceId;
        Guid cdrId;
        await using (var seedDb = CreateDbContext())
        {
            vwceId = await seedDb.SeedInstrumentAsync("VWCE.DE", "Vanguard FTSE All-World", PriceSource.Yahoo, "EUR", cancellationToken, exchange: "XETRA");
            cdrId = await seedDb.SeedInstrumentAsync("CDR.WA", "CD Projekt", PriceSource.Yahoo, "PLN", cancellationToken);
            await seedDb.SeedQuoteAsync(vwceId, new DateOnly(2026, 1, 5), 100m, cancellationToken);
            await seedDb.SeedQuoteAsync(vwceId, new DateOnly(2026, 1, 20), 120m, cancellationToken);
            // A quote after today must never be picked, even though it is the newest overall.
            await seedDb.SeedQuoteAsync(vwceId, new DateOnly(2999, 1, 1), 999m, cancellationToken);
        }

        using var client = Factory.CreateClient();
        var unknownId = Guid.NewGuid();
        var response = await client.PostAsJsonAsync(
            InternalInstrumentsBatchUri, new { instrumentIds = new[] { vwceId, cdrId, unknownId } }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var instruments = document.RootElement.GetProperty("instruments").EnumerateArray().ToList();
        Assert.Equal(2, instruments.Count);
        var vwce = instruments.Single(i => i.GetProperty("instrumentId").GetGuid() == vwceId);
        var cdr = instruments.Single(i => i.GetProperty("instrumentId").GetGuid() == cdrId);
        Assert.Multiple(
            () => Assert.Equal("VWCE.DE", vwce.GetProperty("ticker").GetString()),
            () => Assert.Equal("Vanguard FTSE All-World", vwce.GetProperty("name").GetString()),
            () => Assert.Equal("XETRA", vwce.GetProperty("exchange").GetString()),
            () => Assert.Equal("EUR", vwce.GetProperty("quoteCurrency").GetString()),
            () => Assert.Equal(120m, vwce.GetProperty("lastPrice").GetDecimal()),
            () => Assert.Equal("2026-01-20", vwce.GetProperty("lastPriceDate").GetString()),
            () => Assert.Equal("PLN", cdr.GetProperty("quoteCurrency").GetString()),
            () => Assert.Equal(JsonValueKind.Null, cdr.GetProperty("lastPrice").ValueKind),
            () => Assert.Equal(JsonValueKind.Null, cdr.GetProperty("lastPriceDate").ValueKind));
    }

    [Fact]
    public async Task Metal_LastPriceIsPerGram()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Guid goldId;
        await using (var seedDb = CreateDbContext())
        {
            goldId = await seedDb.SeedInstrumentAsync("GC=F", "Gold (1 g)", PriceSource.Yahoo, "USD", cancellationToken, AssetClass.PreciousMetal);
            await seedDb.SeedQuoteAsync(goldId, new DateOnly(2026, 1, 5), 6220.69536m, cancellationToken);
            await seedDb.SeedQuoteAsync(goldId, new DateOnly(2026, 1, 20), 9331.04304m, cancellationToken);
        }

        using var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            InternalInstrumentsBatchUri, new { instrumentIds = new[] { goldId } }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var gold = Assert.Single(document.RootElement.GetProperty("instruments").EnumerateArray());
        Assert.Equal(300m, gold.GetProperty("lastPrice").GetDecimal());
    }

    [Fact]
    public async Task Batch_ReturnsPreviousClose()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Guid threeQuotesId;
        Guid oneQuoteId;
        await using (var seedDb = CreateDbContext())
        {
            threeQuotesId = await seedDb.SeedInstrumentAsync("SPYL.DE", "SPDR S&P 500", PriceSource.Yahoo, "EUR", cancellationToken);
            oneQuoteId = await seedDb.SeedInstrumentAsync("PKN.WA", "Orlen", PriceSource.Yahoo, "PLN", cancellationToken);
            await seedDb.SeedQuoteAsync(threeQuotesId, new DateOnly(2026, 1, 5), 100m, cancellationToken);
            await seedDb.SeedQuoteAsync(threeQuotesId, new DateOnly(2026, 1, 10), 110m, cancellationToken);
            await seedDb.SeedQuoteAsync(threeQuotesId, new DateOnly(2026, 1, 20), 120m, cancellationToken);
            await seedDb.SeedQuoteAsync(oneQuoteId, new DateOnly(2026, 1, 20), 50m, cancellationToken);
        }

        using var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            InternalInstrumentsBatchUri, new { instrumentIds = new[] { threeQuotesId, oneQuoteId } }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var instruments = document.RootElement.GetProperty("instruments").EnumerateArray().ToList();
        var three = instruments.Single(i => i.GetProperty("instrumentId").GetGuid() == threeQuotesId);
        var one = instruments.Single(i => i.GetProperty("instrumentId").GetGuid() == oneQuoteId);
        Assert.Multiple(
            () => Assert.Equal(120m, three.GetProperty("lastPrice").GetDecimal()),
            () => Assert.Equal(110m, three.GetProperty("previousClose").GetDecimal()),
            () => Assert.Equal("2026-01-10", three.GetProperty("previousCloseDate").GetString()),
            () => Assert.Equal(JsonValueKind.Null, one.GetProperty("previousClose").ValueKind),
            () => Assert.Equal(JsonValueKind.Null, one.GetProperty("previousCloseDate").ValueKind));
    }
}
