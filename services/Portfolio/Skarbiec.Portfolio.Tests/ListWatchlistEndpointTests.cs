using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Securities;
using Skarbiec.Portfolio.Features.Watchlist;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;
using static Skarbiec.Portfolio.Tests.Fixtures.WatchlistApi;

namespace Skarbiec.Portfolio.Tests;

// Every fact calls ListWatchlist directly; items are arranged through AddWatchlistItemAsync and the fake MarketData clients.
public sealed class ListWatchlistEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task List_ComputesPriceFields()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(FixedTodayUtc);
        Factory.InstrumentQuoteLookupClient.WithRate("USD", 4.00m);
        var etfId = ScriptInstrument(AssetClass.Etf, "USD", "ETFA", 110m, "NYSE", "Alpha ETF", new DateOnly(2026, 1, 30), 100m, new DateOnly(2026, 1, 29));
        var stockId = ScriptInstrument(AssetClass.Stock, "PLN", "STKB", 50m, "GPW", "Beta stock", new DateOnly(2026, 1, 30));
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.AddWatchlistItemAsync(etfId, cancellationToken, note: "core", targetPrice: 120m);
        await client.AddWatchlistItemAsync(stockId, cancellationToken);

        var response = await client.GetAsync(WatchlistUri, cancellationToken);
        var etfsOnly = await client.GetAsync($"{WatchlistUri}?assetClass=Etf", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.Content.ReadFromJsonAsync<List<WatchlistItemResponse>>(cancellationToken))!;
        Assert.Equal(["ETFA", "STKB"], items.Select(i => i.Ticker).ToArray());
        var etf = items[0];
        Assert.Multiple(
            () => Assert.Equal(110m, etf.LastPrice),
            () => Assert.Equal(new DateOnly(2026, 1, 30), etf.LastPriceDate),
            () => Assert.Equal(100m, etf.PreviousClose),
            () => Assert.Equal(10m, etf.DailyChange),
            () => Assert.Equal(10.00m, etf.DailyChangePercent),
            () => Assert.Equal(440.00m, etf.LastPricePln),
            () => Assert.Equal(-8.33m, etf.TargetDistancePercent),
            () => Assert.True(etf.AtOrBelowTarget),
            () => Assert.Null(etf.PriceUnavailableReason));
        var stock = items[1];
        Assert.Multiple(
            () => Assert.Equal(50m, stock.LastPrice),
            () => Assert.Equal(50.00m, stock.LastPricePln),
            () => Assert.Null(stock.PreviousClose),
            () => Assert.Null(stock.DailyChange),
            () => Assert.Null(stock.DailyChangePercent),
            () => Assert.Null(stock.TargetDistancePercent),
            () => Assert.False(stock.AtOrBelowTarget),
            () => Assert.Null(stock.PriceUnavailableReason));
        Assert.Equal(HttpStatusCode.OK, etfsOnly.StatusCode);
        var etfItems = (await etfsOnly.Content.ReadFromJsonAsync<List<WatchlistItemResponse>>(cancellationToken))!;
        Assert.Equal("ETFA", Assert.Single(etfItems).Ticker);
    }

    [Fact]
    public async Task List_FiltersByClassAndOrdersByTicker()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(FixedTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        foreach (var (assetClass, ticker) in new[] { (AssetClass.Etf, "ZZZ"), (AssetClass.Stock, "BBB"), (AssetClass.Etf, "AAA") })
        {
            await client.AddWatchlistItemAsync(ScriptInstrument(assetClass, "PLN", ticker), cancellationToken);
        }

        var all = await client.GetAsync(WatchlistUri, cancellationToken);
        var etfs = await client.GetAsync($"{WatchlistUri}?assetClass=Etf", cancellationToken);
        var stocks = await client.GetAsync($"{WatchlistUri}?assetClass=Stock", cancellationToken);

        Assert.Equal(["AAA", "BBB", "ZZZ"], Tickers(await all.Content.ReadFromJsonAsync<List<WatchlistItemResponse>>(cancellationToken)));
        Assert.Equal(["AAA", "ZZZ"], Tickers(await etfs.Content.ReadFromJsonAsync<List<WatchlistItemResponse>>(cancellationToken)));
        Assert.Equal(["BBB"], Tickers(await stocks.Content.ReadFromJsonAsync<List<WatchlistItemResponse>>(cancellationToken)));
    }

    [Fact]
    public async Task List_UnsupportedClass_Returns400()
    {
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync($"{WatchlistUri}?assetClass=Cash", TestContext.Current.CancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, "Validation.UnsupportedAssetClass", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task List_ReturnsUnarchivedHoldings()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(FixedTodayUtc);
        var instrumentId = ScriptInstrument(AssetClass.Etf, "PLN", "HELD");
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.AddWatchlistItemAsync(instrumentId, cancellationToken);
        var zetaId = await client.CreateAccountAsync(cancellationToken, "Zeta");
        var alphaId = await client.CreateAccountAsync(cancellationToken, "Alpha");
        var closedId = await client.CreateAccountAsync(cancellationToken, "Beta closed");
        await client.FundAccountAsync(zetaId, cancellationToken);
        await client.FundAccountAsync(closedId, cancellationToken);
        var zetaAsset = await AddQuotedHoldingAsync(client, zetaId, cancellationToken, AssetClass.Etf, "PLN", "Zeta shares", instrumentId: instrumentId);
        var alphaAsset = await AddQuotedHoldingAsync(client, alphaId, cancellationToken, AssetClass.Etf, "PLN", "Alpha shares", instrumentId: instrumentId);
        var closedAsset = await AddQuotedHoldingAsync(client, closedId, cancellationToken, AssetClass.Etf, "PLN", "Closed shares", instrumentId: instrumentId);
        await client.RecordTransactionAsync(zetaAsset, TransactionType.Buy, 3m, new DateOnly(2026, 1, 5), cancellationToken);
        await client.RecordTransactionAsync(closedAsset, TransactionType.Buy, 2m, new DateOnly(2026, 1, 5), cancellationToken);
        await client.RecordTransactionAsync(closedAsset, TransactionType.Sell, 2m, new DateOnly(2026, 1, 6), cancellationToken);
        await client.ArchiveAccountAsync(closedId, cancellationToken);

        var response = await client.GetAsync(WatchlistUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await response.Content.ReadFromJsonAsync<List<WatchlistItemResponse>>(cancellationToken))!);
        Assert.Equal([alphaAsset, zetaAsset], item.Holdings.Select(h => h.AssetId).ToArray());
        Assert.Equal(["Alpha", "Zeta"], item.Holdings.Select(h => h.AccountName).ToArray());
        Assert.Equal([alphaId, zetaId], item.Holdings.Select(h => h.AccountId).ToArray());
        Assert.Equal(["Alpha shares", "Zeta shares"], item.Holdings.Select(h => h.AssetName).ToArray());
        Assert.Equal([0m, 3m], item.Holdings.Select(h => h.Quantity).ToArray());
        Assert.Equal(3m, item.HeldQuantity);
    }

    [Fact]
    public async Task List_MarketDataDown_FailsSoft()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(FixedTodayUtc);
        var instrumentId = ScriptInstrument(AssetClass.Etf, "USD", "DOWN", 110m, "NYSE", "Down ETF", previousClose: 100m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.AddWatchlistItemAsync(instrumentId, cancellationToken, targetPrice: 120m);
        Factory.InstrumentQuoteLookupClient.WithUnavailable();

        var response = await client.GetAsync(WatchlistUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await response.Content.ReadFromJsonAsync<List<WatchlistItemResponse>>(cancellationToken))!);
        Assert.Multiple(
            () => Assert.Equal(SecurityPriceUnavailableReason.MarketDataUnavailable, item.PriceUnavailableReason),
            () => Assert.Equal("DOWN", item.Ticker),
            () => Assert.Equal("Down ETF", item.Name),
            () => Assert.Null(item.LastPrice),
            () => Assert.Null(item.LastPriceDate),
            () => Assert.Null(item.PreviousClose),
            () => Assert.Null(item.DailyChange),
            () => Assert.Null(item.DailyChangePercent),
            () => Assert.Null(item.LastPricePln),
            () => Assert.Null(item.TargetDistancePercent),
            () => Assert.False(item.AtOrBelowTarget));
    }

    [Fact]
    public async Task List_NoQuote()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(FixedTodayUtc);
        var instrumentId = ScriptInstrument(AssetClass.Stock, "PLN", "NOQ", lastPrice: null);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.AddWatchlistItemAsync(instrumentId, cancellationToken, targetPrice: 10m);

        var response = await client.GetAsync(WatchlistUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await response.Content.ReadFromJsonAsync<List<WatchlistItemResponse>>(cancellationToken))!);
        Assert.Multiple(
            () => Assert.Equal(SecurityPriceUnavailableReason.NoQuote, item.PriceUnavailableReason),
            () => Assert.Null(item.LastPrice),
            () => Assert.Null(item.LastPricePln),
            () => Assert.Null(item.TargetDistancePercent),
            () => Assert.False(item.AtOrBelowTarget));
    }

    [Fact]
    public async Task List_FxRateMissing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(FixedTodayUtc);
        var instrumentId = ScriptInstrument(AssetClass.Etf, "USD", "NOFX", 110m, "NYSE", "No rate ETF");
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.AddWatchlistItemAsync(instrumentId, cancellationToken);

        var response = await client.GetAsync(WatchlistUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await response.Content.ReadFromJsonAsync<List<WatchlistItemResponse>>(cancellationToken))!);
        Assert.Multiple(
            () => Assert.Equal(110m, item.LastPrice),
            () => Assert.Null(item.LastPricePln),
            () => Assert.Equal(SecurityPriceUnavailableReason.FxRateMissing, item.PriceUnavailableReason));
    }

    [Fact]
    public async Task List_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync(WatchlistUri, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string[] Tickers(List<WatchlistItemResponse>? items) => [.. items!.Select(i => i.Ticker)];
}
