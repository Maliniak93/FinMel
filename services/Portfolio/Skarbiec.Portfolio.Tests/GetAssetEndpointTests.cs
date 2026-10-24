using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class GetAssetEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Get_ExistingAsset_ReturnsIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);
        var assetId = await client.AddAssetAsync(accountId, cancellationToken, name: "Shares");

        var response = await client.GetAsync(AssetUri(assetId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal(assetId, body!.Id);
        Assert.Equal("Shares", body.Name);
    }

    [Fact]
    public async Task Get_NonExistentAsset_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync(AssetUri(Guid.NewGuid()), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_AssetWithTransactions_ReportsTransactionCountFromTransactions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        await client.RecordTransactionAsync(assetId, TransactionType.Buy, 1m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.RecordTransactionAsync(assetId, TransactionType.Buy, 1m, new DateOnly(2026, 1, 2), cancellationToken);

        var response = await client.GetAsync(AssetUri(assetId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal(2, body!.TransactionCount);
    }

    [Fact]
    public async Task Get_Stock_ReturnsCostAverageAndDividend()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var holding = await client.CreateBrokerageHoldingAsync(cancellationToken, assetClass: AssetClass.Stock);
        await client.RecordBrokerageTradeAsync(holding.AssetId, cancellationToken, quantity: 10m, unitPrice: 100m, date: CashDay1);
        await client.RecordBrokerageTradeAsync(
            holding.AssetId, cancellationToken, TransactionType.Sell, quantity: 4m, unitPrice: 120m, date: CashDay2);
        var dividend = await client.PostAsJsonAsync(
            TransactionsUri(holding.AssetId),
            NewBrokerageTradeRequest(TransactionType.Dividend, 25m, 1m, CashDay3),
            cancellationToken);
        dividend.EnsureSuccessStatusCode();

        var response = await client.GetAsync(AssetUri(holding.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken))!;
        Assert.Multiple(
            () => Assert.Equal(600m, body.CostPln),
            () => Assert.Equal(100m, body.AveragePricePln),
            () => Assert.Equal(25m, body.DividendPln),
            () => Assert.Equal(6m, body.UnitCount));
    }

    [Fact]
    public async Task Get_Metal_ReturnsCostAndAverageWithoutDividend()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var setup = await client.CreateCashAndMetalAsync(cancellationToken);
        await client.RecordMetalTransactionWithCashAsync(
            setup.MetalAssetId, setup.CashAssetId, cancellationToken, pieces: 2m, totalAmount: 2_400.00m);

        var response = await client.GetAsync(AssetUri(setup.MetalAssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken))!;
        Assert.Multiple(
            () => Assert.Equal(2_400m, body.CostPln),
            () => Assert.Equal(1_200m, body.AveragePricePln),
            () => Assert.Null(body.DividendPln),
            () => Assert.Equal(2m, body.UnitCount));
    }

    [Fact]
    public async Task Get_Bond_ReturnsBookValueAndPricePerBond()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var funded = await client.CreateFundedBondAsync(
            cancellationToken, request: NewBondRequest(bondCount: 50, purchasePricePerBond: 100m));

        var response = await client.GetAsync(AssetUri(funded.Bond.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken))!;
        Assert.Multiple(
            () => Assert.Equal(funded.Bond.BookValue, body.CostPln),
            () => Assert.Equal(5_000m, body.CostPln),
            () => Assert.Equal(100m, body.AveragePricePln),
            () => Assert.Equal(50m, body.UnitCount),
            () => Assert.Null(body.DividendPln));
    }

    [Fact]
    public async Task Get_CurrencyValuedClass_HasNoKpiFields()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var cashId = await client.AddCashAssetAsync(cancellationToken);
        var deposit = await client.AddDepositAsync(cancellationToken);
        var bank = await client.CreateBankAccountAsync(cancellationToken, balance: 500m);

        foreach (var assetId in new[] { cashId, deposit.AssetId, bank.MoneyAssetId })
        {
            var response = await client.GetAsync(AssetUri(assetId), cancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = (await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken))!;
            Assert.Multiple(
                () => Assert.Null(body.CostPln),
                () => Assert.Null(body.AveragePricePln),
                () => Assert.Null(body.DividendPln),
                () => Assert.Null(body.UnitCount));
        }
    }

    [Fact]
    public async Task Get_AssetWithOutOfSetCurrency_ReadsBackUnchanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await using (var seedContext = CreateDbContext(ownerId))
        {
            seedContext.Assets.Add(new Asset
            {
                Id = assetId,
                AssetClass = AssetClass.Cash,
                ValuationMode = AssetValuationMode.CurrencyValued,
                Name = "Legacy GBP cash",
                Currency = "GBP"
            });
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        using var client = Factory.CreateAuthenticatedClient(ownerId);
        var response = await client.GetAsync(AssetUri(assetId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal("GBP", body!.Currency);
    }
}
