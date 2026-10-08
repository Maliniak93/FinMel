using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Securities;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

// Every fact calls ListSecurities directly; holdings are arranged through the fixture helpers and the fake MarketData clients.
[Collection(TestingDefaults.CollectionName)]
public sealed class ListSecuritiesEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Etf_ValuesAndGains()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        Factory.FxRateLookupClient
            .WithRate("EUR", new DateOnly(2026, 1, 10), 4.00m)
            .WithRate("EUR", new DateOnly(2026, 1, 20), 4.20m);
        Factory.InstrumentQuoteLookupClient.WithRate("EUR", 4.30m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken, name: "Main");
        var euroEtf = await AddQuotedHoldingAsync(
            client, portfolioId, cancellationToken, AssetClass.Etf, "EUR", "A ETF", "VWCE", 120m, "XETRA", new DateOnly(2026, 1, 30));
        await client.RecordTransactionAsync(portfolioId, euroEtf, TransactionType.Buy, 10m, new DateOnly(2026, 1, 10), cancellationToken, unitPrice: 100m);
        await client.RecordTransactionAsync(portfolioId, euroEtf, TransactionType.Buy, 10m, new DateOnly(2026, 1, 20), cancellationToken, unitPrice: 120m);
        await client.RecordTransactionAsync(portfolioId, euroEtf, TransactionType.Sell, 5m, new DateOnly(2026, 1, 25), cancellationToken, unitPrice: 125m);
        var zlotyEtf = await AddQuotedHoldingAsync(
            client, portfolioId, cancellationToken, AssetClass.Etf, "PLN", "B ETF", "ETFBW", 60m, "GPW", new DateOnly(2026, 1, 30));
        await client.RecordTransactionAsync(portfolioId, zlotyEtf, TransactionType.Buy, 10m, new DateOnly(2026, 1, 5), cancellationToken, unitPrice: 50m);

        var etfs = await client.GetAsync(AllSecuritiesUri(AssetClass.Etf), cancellationToken);
        var stocks = await client.GetAsync(AllSecuritiesUri(AssetClass.Stock), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, etfs.StatusCode);
        var body = (await etfs.Content.ReadFromJsonAsync<SecuritiesResponse>(cancellationToken))!;
        Assert.Equal([euroEtf, zlotyEtf], body.Holdings.Select(h => h.AssetId).ToArray());
        var euro = body.Holdings[0];
        Assert.Multiple(
            () => Assert.Equal(portfolioId, euro.PortfolioId),
            () => Assert.Equal("Main", euro.PortfolioName),
            () => Assert.Equal("A ETF", euro.Name),
            () => Assert.Equal("VWCE", euro.Ticker),
            () => Assert.Equal("XETRA", euro.Exchange),
            () => Assert.Equal("EUR", euro.Currency),
            () => Assert.Equal(15m, euro.Quantity),
            () => Assert.Equal(110m, euro.AverageBuyPrice),
            () => Assert.Equal(6780m, euro.CostPln),
            () => Assert.Equal(120m, euro.LastPrice),
            () => Assert.Equal(new DateOnly(2026, 1, 30), euro.LastPriceDate),
            () => Assert.Equal(7740.00m, euro.ValuePln),
            () => Assert.Equal(150.00m, euro.UnrealizedPl),
            () => Assert.Equal(9.09m, euro.UnrealizedPlPercent),
            () => Assert.Equal(960m, euro.UnrealizedPlPln),
            () => Assert.Null(euro.PriceUnavailableReason));
        var zloty = body.Holdings[1];
        Assert.Multiple(
            () => Assert.Equal(600.00m, zloty.ValuePln),
            () => Assert.Equal(100.00m, zloty.UnrealizedPl),
            () => Assert.Equal(20.00m, zloty.UnrealizedPlPercent),
            () => Assert.Equal(100m, zloty.UnrealizedPlPln));
        Assert.Multiple(
            () => Assert.Equal(8340.00m, body.Totals.ValuePln),
            () => Assert.Equal(7280m, body.Totals.CostPln),
            () => Assert.Equal(1060m, body.Totals.UnrealizedPlPln));
        Assert.Equal(HttpStatusCode.OK, stocks.StatusCode);
        Assert.Empty((await stocks.Content.ReadFromJsonAsync<SecuritiesResponse>(cancellationToken))!.Holdings);
    }

    [Fact]
    public async Task List_OneBatchCallEach()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        Factory.InstrumentQuoteLookupClient.WithRate("EUR", 4.30m).WithRate("USD", 3.90m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var firstPortfolio = await client.CreatePortfolioAsync(cancellationToken, name: "First");
        var secondPortfolio = await client.CreatePortfolioAsync(cancellationToken, name: "Second");
        var euro = await AddQuotedHoldingAsync(client, firstPortfolio, cancellationToken, currency: "EUR", name: "Euro stock", ticker: "SAP");
        var dollar = await AddQuotedHoldingAsync(client, firstPortfolio, cancellationToken, currency: "USD", name: "Dollar stock", ticker: "AAPL");
        var zloty = await AddQuotedHoldingAsync(client, secondPortfolio, cancellationToken, currency: "PLN", name: "Zloty stock", ticker: "CDR");
        foreach (var (portfolio, asset) in new[] { (firstPortfolio, euro), (firstPortfolio, dollar), (secondPortfolio, zloty) })
        {
            await client.RecordTransactionAsync(portfolio, asset, TransactionType.Buy, 1m, new DateOnly(2026, 1, 5), cancellationToken);
        }

        var response = await client.GetAsync(AllSecuritiesUri(AssetClass.Stock), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<SecuritiesResponse>(cancellationToken))!;
        Assert.Equal(3, body.Holdings.Count);
        var call = Assert.Single(Factory.InstrumentQuoteLookupClient.Calls);
        Assert.Equal(3, call.Distinct().Count());
    }

    [Fact]
    public async Task MissingQuoteOrRate_ReasonAndNulls()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        Factory.InstrumentQuoteLookupClient.WithRate("USD", 4.00m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var noQuote = await AddQuotedHoldingAsync(
            client, portfolioId, cancellationToken, currency: "USD", name: "A no quote", ticker: "NOQ", lastPrice: null);
        var noRate = await AddQuotedHoldingAsync(
            client, portfolioId, cancellationToken, currency: "EUR", name: "B no rate", ticker: "NOR", lastPrice: 50m);
        var healthy = await AddQuotedHoldingAsync(
            client, portfolioId, cancellationToken, currency: "USD", name: "C healthy", ticker: "OK", lastPrice: 20m);
        foreach (var asset in new[] { noQuote, noRate, healthy })
        {
            await client.RecordTransactionAsync(portfolioId, asset, TransactionType.Buy, 10m, new DateOnly(2026, 1, 5), cancellationToken, unitPrice: 10m);
        }

        var response = await client.GetAsync(AllSecuritiesUri(AssetClass.Stock), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<SecuritiesResponse>(cancellationToken))!;
        var first = body.Holdings.Single(h => h.AssetId == noQuote);
        var second = body.Holdings.Single(h => h.AssetId == noRate);
        var third = body.Holdings.Single(h => h.AssetId == healthy);
        Assert.Multiple(
            () => Assert.Equal("NoQuote", first.PriceUnavailableReason?.ToString()),
            () => Assert.Null(first.LastPrice),
            () => Assert.Null(first.LastPriceDate),
            () => Assert.Null(first.ValuePln),
            () => Assert.Null(first.UnrealizedPl),
            () => Assert.Null(first.UnrealizedPlPercent),
            () => Assert.Null(first.UnrealizedPlPln),
            () => Assert.Equal(10m, first.Quantity),
            () => Assert.Equal(10m, first.AverageBuyPrice),
            () => Assert.Equal("FxRateMissing", second.PriceUnavailableReason?.ToString()),
            () => Assert.Null(second.LastPrice),
            () => Assert.Null(second.ValuePln),
            () => Assert.Null(second.UnrealizedPlPln),
            () => Assert.Null(third.PriceUnavailableReason));
        Assert.Multiple(
            () => Assert.Equal(800.00m, body.Totals.ValuePln),
            () => Assert.Equal(700m, body.Totals.UnrealizedPlPln));
    }

    [Fact]
    public async Task MarketDataDown_FailsSoft()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        Factory.InstrumentQuoteLookupClient.WithUnavailable();
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var first = await AddQuotedHoldingAsync(client, portfolioId, cancellationToken, name: "First", ticker: "ONE");
        var second = await AddQuotedHoldingAsync(client, portfolioId, cancellationToken, name: "Second", ticker: "TWO");
        await client.RecordTransactionAsync(portfolioId, first, TransactionType.Buy, 4m, new DateOnly(2026, 1, 5), cancellationToken, unitPrice: 25m);
        await client.RecordTransactionAsync(portfolioId, second, TransactionType.Buy, 2m, new DateOnly(2026, 1, 6), cancellationToken, unitPrice: 30m);

        var response = await client.GetAsync(AllSecuritiesUri(AssetClass.Stock), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<SecuritiesResponse>(cancellationToken))!;
        Assert.Equal(2, body.Holdings.Count);
        Assert.All(body.Holdings, h =>
        {
            Assert.Equal("MarketDataUnavailable", h.PriceUnavailableReason?.ToString());
            Assert.Null(h.Ticker);
            Assert.Null(h.Exchange);
            Assert.Null(h.LastPrice);
            Assert.Null(h.ValuePln);
            Assert.Null(h.UnrealizedPl);
            Assert.Null(h.UnrealizedPlPln);
        });
        Assert.Multiple(
            () => Assert.Equal(4m, body.Holdings[0].Quantity),
            () => Assert.Equal(25m, body.Holdings[0].AverageBuyPrice),
            () => Assert.Equal(2m, body.Holdings[1].Quantity),
            () => Assert.Equal(30m, body.Holdings[1].AverageBuyPrice));
    }

    [Fact]
    public async Task Excludes_ArchivedManualAndOtherClasses()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken, name: "Live");
        var live = await AddQuotedHoldingAsync(client, portfolioId, cancellationToken, name: "Live stock", ticker: "LIVE");
        var archived = await AddQuotedHoldingAsync(client, portfolioId, cancellationToken, name: "Archived stock", ticker: "OLD");
        await client.ArchiveAssetAsync(portfolioId, archived, cancellationToken);
        await client.AddAssetAsync(portfolioId, cancellationToken, name: "Manual stock", assetClass: AssetClass.Stock, manualValue: 500m);
        await AddQuotedHoldingAsync(client, portfolioId, cancellationToken, AssetClass.Crypto, name: "Bitcoin", ticker: "BTC");
        var archivedPortfolioId = await client.CreatePortfolioAsync(cancellationToken, name: "Closed");
        await AddQuotedHoldingAsync(client, archivedPortfolioId, cancellationToken, name: "Stock in closed portfolio", ticker: "CLS");
        await client.ArchivePortfolioAsync(archivedPortfolioId, cancellationToken);

        var response = await client.GetAsync(AllSecuritiesUri(AssetClass.Stock), cancellationToken);
        var unsupported = await client.GetAsync(AllSecuritiesUri(AssetClass.Crypto), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<SecuritiesResponse>(cancellationToken))!;
        Assert.Equal(live, Assert.Single(body.Holdings).AssetId);
        await unsupported.AssertProblemAsync(HttpStatusCode.BadRequest, "Validation.UnsupportedAssetClass", cancellationToken);
    }

    [Fact]
    public async Task List_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync(AllSecuritiesUri(AssetClass.Stock), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
