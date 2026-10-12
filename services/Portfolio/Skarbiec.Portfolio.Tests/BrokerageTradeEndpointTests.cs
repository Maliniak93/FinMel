using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.UpdateTransaction;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

// The record, update and delete transaction slices are called directly; fixture helpers only arrange.
public sealed class BrokerageTradeEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Buy_DebitsAccountCashWithFee()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var holding = await client.CreateBrokerageHoldingAsync(cancellationToken);

        var response = await client.PostAsJsonAsync(
            TransactionsUri(holding.AssetId),
            NewBrokerageTradeRequest(quantity: 10m, unitPrice: 120m, date: CashDay1, feeAmount: 3m),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var buyResponse = (await response.Content.ReadFromJsonAsync<TransactionResponse>(cancellationToken))!;
        Assert.Equal(3m, buyResponse.FeeAmount);
        Assert.Equal(3_797m, await client.GetCashBalanceAsync(holding.AccountId, cancellationToken));
        await using var dbContext = CreateDbContext(userId);
        var legs = await dbContext.Transactions.Where(t => t.TransferId != null).ToListAsync(cancellationToken);
        Assert.Equal(2, legs.Count);
        Assert.Single(legs.Select(t => t.TransferId).Distinct());
        var buy = Assert.Single(legs, l => l.AssetId == holding.AssetId);
        Assert.Equal(TransactionType.Buy, buy.Type);
        Assert.Equal(3m, buy.FeeAmount);
        Assert.Equal(1m, buy.FxRateToPln);
        var withdraw = Assert.Single(legs, l => l.AssetId == holding.CashAssetId);
        Assert.Equal(TransactionType.Withdraw, withdraw.Type);
        Assert.Equal(1_203m, withdraw.Quantity);
        Assert.Equal(1m, withdraw.UnitPriceAmount);
        Assert.Equal(1m, withdraw.FxRateToPln);
        Assert.Equal(CashDay1, withdraw.Date);
        var etf = Assert.Single((await client.GetSecuritiesAsync(AssetClass.Etf, cancellationToken)).Holdings);
        Assert.Equal(1_203m, etf.CostPln);
        Assert.Equal(120.3m, etf.AverageBuyPrice);
    }

    [Fact]
    public async Task Sell_CreditsNetOfFee()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var holding = await client.CreateBrokerageHoldingAsync(cancellationToken);
        await client.RecordBrokerageTradeAsync(holding.AssetId, cancellationToken, quantity: 10m, unitPrice: 120m, feeAmount: 3m);

        var response = await client.PostAsJsonAsync(
            TransactionsUri(holding.AssetId),
            NewBrokerageTradeRequest(TransactionType.Sell, 4m, 130m, CashDay2, feeAmount: 3m),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(4_314m, await client.GetCashBalanceAsync(holding.AccountId, cancellationToken));
        await using var dbContext = CreateDbContext(userId);
        var deposit = await dbContext.Transactions.SingleAsync(
            t => t.AssetId == holding.CashAssetId && t.TransferId != null && t.Type == TransactionType.Deposit,
            cancellationToken);
        Assert.Equal(517m, deposit.Quantity);
        Assert.Equal(CashDay2, deposit.Date);
        var etf = Assert.Single((await client.GetSecuritiesAsync(AssetClass.Etf, cancellationToken)).Holdings);
        Assert.Equal(6m, etf.Quantity);
        Assert.Equal(721.8m, etf.CostPln);
    }

    [Fact]
    public async Task FeeAboveGross_Returns400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var holding = await client.CreateBrokerageHoldingAsync(cancellationToken);
        await client.RecordBrokerageTradeAsync(holding.AssetId, cancellationToken, quantity: 10m, unitPrice: 120m);
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.PostAsJsonAsync(
            TransactionsUri(holding.AssetId),
            NewBrokerageTradeRequest(TransactionType.Sell, 1m, 2m, CashDay2, feeAmount: 5m),
            cancellationToken);

        await response.AssertProblemAsync(
            HttpStatusCode.BadRequest, PortfolioAssertions.FeeExceedsProceedsErrorCode, cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
    }

    [Fact]
    public async Task Buy_ExceedsCashOnAnyDay_Returns400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var holding = await client.CreateBrokerageHoldingAsync(cancellationToken, cashBalance: 1_000m);
        await client.RecordTransactionAsync(
            holding.CashAssetId, TransactionType.Withdraw, 500m, CashDay2, cancellationToken, unitPrice: 1m);
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var tooExpensive = await client.PostAsJsonAsync(
            TransactionsUri(holding.AssetId),
            NewBrokerageTradeRequest(quantity: 10m, unitPrice: 120m, date: CashDay1),
            cancellationToken);
        var breaksLaterDay = await client.PostAsJsonAsync(
            TransactionsUri(holding.AssetId),
            NewBrokerageTradeRequest(quantity: 9m, unitPrice: 100m, date: CashDay1),
            cancellationToken);

        await tooExpensive.AssertInsufficientFundsAsync(cancellationToken);
        await breaksLaterDay.AssertInsufficientFundsAsync(cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
        Assert.Equal(500m, await client.GetCashBalanceAsync(holding.AccountId, cancellationToken));
    }

    [Fact]
    public async Task ForeignBuy_SettlesInPln()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        Factory.FxRateLookupClient.WithRate("USD", CashDay1, 4.00m);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var holding = await client.CreateBrokerageHoldingAsync(cancellationToken, assetClass: AssetClass.Stock, currency: "USD");

        var response = await client.PostAsJsonAsync(
            TransactionsUri(holding.AssetId),
            NewBrokerageTradeRequest(quantity: 2m, unitPrice: 100m, date: CashDay1, feeAmount: 5m),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(4_195m, await client.GetCashBalanceAsync(holding.AccountId, cancellationToken));
        await using var dbContext = CreateDbContext(userId);
        var legs = await dbContext.Transactions.Where(t => t.TransferId != null).ToListAsync(cancellationToken);
        var buy = Assert.Single(legs, l => l.AssetId == holding.AssetId);
        Assert.Equal(4.00m, buy.FxRateToPln);
        Assert.Equal(5m, buy.FeeAmount);
        var withdraw = Assert.Single(legs, l => l.AssetId == holding.CashAssetId);
        Assert.Equal(TransactionType.Withdraw, withdraw.Type);
        Assert.Equal(805m, withdraw.Quantity);
        Assert.Equal(1m, withdraw.UnitPriceAmount);
        Assert.Equal(1m, withdraw.FxRateToPln);
    }

    [Fact]
    public async Task ForeignBuy_NoRate_Returns400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        Factory.FxRateLookupClient.WithNotFound("USD");
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var holding = await client.CreateBrokerageHoldingAsync(cancellationToken, assetClass: AssetClass.Stock, currency: "USD");
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.PostAsJsonAsync(
            TransactionsUri(holding.AssetId),
            NewBrokerageTradeRequest(quantity: 2m, unitPrice: 100m, date: CashDay1, feeAmount: 5m),
            cancellationToken);

        await response.AssertProblemAsync(
            HttpStatusCode.BadRequest, PortfolioAssertions.FxRateMissingErrorCode, cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
        Assert.Equal(5_000m, await client.GetCashBalanceAsync(holding.AccountId, cancellationToken));
    }

    [Fact]
    public async Task Dividend_NetPln_AfterFullSale()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        Factory.FxRateLookupClient.WithRate("USD", 4.00m);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var holding = await client.CreateBrokerageHoldingAsync(cancellationToken, assetClass: AssetClass.Stock, currency: "USD");
        await client.RecordBrokerageTradeAsync(holding.AssetId, cancellationToken, quantity: 2m, unitPrice: 100m, date: CashDay1);
        await client.RecordBrokerageTradeAsync(
            holding.AssetId, cancellationToken, TransactionType.Sell, quantity: 2m, unitPrice: 110m, date: CashDay2);
        var cashBefore = await client.GetCashBalanceAsync(holding.AccountId, cancellationToken);
        var basisBefore = await client.GetSecuritiesAsync(AssetClass.Stock, cancellationToken);

        var response = await client.PostAsJsonAsync(
            TransactionsUri(holding.AssetId),
            NewBrokerageTradeRequest(TransactionType.Dividend, 37.40m, 1m, CashDay3),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dividend = (await response.Content.ReadFromJsonAsync<TransactionResponse>(cancellationToken))!;
        Assert.Equal(37.40m, dividend.ValuePln);
        Assert.Equal(cashBefore + 37.40m, await client.GetCashBalanceAsync(holding.AccountId, cancellationToken));
        await using var dbContext = CreateDbContext(userId);
        var security = await dbContext.Transactions.SingleAsync(t => t.Id == dividend.Id, cancellationToken);
        Assert.Equal(1m, security.FxRateToPln);
        var cashLeg = await dbContext.Transactions.SingleAsync(
            t => t.AssetId == holding.CashAssetId && t.TransferId == security.TransferId, cancellationToken);
        Assert.Equal(TransactionType.Deposit, cashLeg.Type);
        Assert.Equal(37.40m, cashLeg.Quantity);
        var basisAfter = await client.GetSecuritiesAsync(AssetClass.Stock, cancellationToken);
        Assert.Equal(basisBefore.Holdings, basisAfter.Holdings);
    }

    [Fact]
    public async Task Update_ResizesCashLeg()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var holding = await client.CreateBrokerageHoldingAsync(cancellationToken);
        var buy = await client.RecordBrokerageTradeAsync(
            holding.AssetId, cancellationToken, quantity: 10m, unitPrice: 120m, feeAmount: 3m);
        var update = new UpdateTransactionRequest
        {
            Type = TransactionType.Buy,
            Quantity = 10m,
            UnitPrice = 110m,
            Date = CashDay2,
            FeeAmount = 3m
        };

        var response = await client.PutAsJsonAsync(TransactionUri(holding.AssetId, buy.Id), update, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3_897m, await client.GetCashBalanceAsync(holding.AccountId, cancellationToken));
        await using var dbContext = CreateDbContext(userId);
        var legs = await dbContext.Transactions.Where(t => t.TransferId != null).ToListAsync(cancellationToken);
        Assert.Equal(2, legs.Count);
        var withdraw = Assert.Single(legs, l => l.AssetId == holding.CashAssetId);
        Assert.Equal(1_103m, withdraw.Quantity);
        Assert.Equal(CashDay2, withdraw.Date);
        Assert.Equal(3m, Assert.Single(legs, l => l.AssetId == holding.AssetId).FeeAmount);
    }

    [Fact]
    public async Task DeleteSell_SpentProceeds_Returns400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var holding = await client.CreateBrokerageHoldingAsync(cancellationToken, cashBalance: 1_000m);
        await client.RecordBrokerageTradeAsync(holding.AssetId, cancellationToken, quantity: 10m, unitPrice: 100m, date: CashDay1);
        var sell = await client.RecordBrokerageTradeAsync(
            holding.AssetId, cancellationToken, TransactionType.Sell, quantity: 10m, unitPrice: 100m, date: CashDay2);
        await client.RecordTransactionAsync(
            holding.CashAssetId, TransactionType.Withdraw, 1_000m, CashDay3, cancellationToken, unitPrice: 1m);
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.DeleteAsync(TransactionUri(holding.AssetId, sell.Id), cancellationToken);

        await response.AssertInsufficientFundsAsync(cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
    }

    [Fact]
    public async Task Delete_RemovesBothLegs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var holding = await client.CreateBrokerageHoldingAsync(cancellationToken);
        var buy = await client.RecordBrokerageTradeAsync(holding.AssetId, cancellationToken, feeAmount: 3m);

        var response = await client.DeleteAsync(TransactionUri(holding.AssetId, buy.Id), cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Delete answered {(int)response.StatusCode}.");
        Assert.Equal(5_000m, await client.GetCashBalanceAsync(holding.AccountId, cancellationToken));
        Assert.Equal(0m, (await client.GetAssetAsync(holding.AssetId, cancellationToken)).Quantity);
        await using var dbContext = CreateDbContext(userId);
        Assert.False(await dbContext.Transactions.AnyAsync(t => t.TransferId != null, cancellationToken));
        Assert.Empty(await dbContext.Transactions.Where(t => t.AssetId == holding.AssetId).ToListAsync(cancellationToken));
    }

    [Fact]
    public async Task RejectedShapes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var holding = await client.CreateBrokerageHoldingAsync(cancellationToken, assetClass: AssetClass.Stock);
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var withCashLink = await client.PostAsJsonAsync(
            TransactionsUri(holding.AssetId),
            NewBrokerageTradeRequest(cashAssetId: holding.CashAssetId),
            cancellationToken);
        var deposit = await client.PostAsJsonAsync(
            TransactionsUri(holding.AssetId),
            NewBrokerageTradeRequest(TransactionType.Deposit, 5m, 1m),
            cancellationToken);
        var dividendWithFee = await client.PostAsJsonAsync(
            TransactionsUri(holding.AssetId),
            NewBrokerageTradeRequest(TransactionType.Dividend, 10m, 1m, feeAmount: 1m),
            cancellationToken);

        await withCashLink.AssertCashLinkNotAllowedAsync(cancellationToken);
        await deposit.AssertProblemAsync(
            HttpStatusCode.BadRequest, PortfolioAssertions.TransactionTypeNotAllowedErrorCode, cancellationToken);
        await dividendWithFee.AssertProblemAsync(
            HttpStatusCode.BadRequest, PortfolioAssertions.FeeNotAllowedErrorCode, cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
        Assert.Equal(5_000m, await client.GetCashBalanceAsync(holding.AccountId, cancellationToken));
    }

    [Fact]
    public async Task Trade_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var holding = await owner.CreateBrokerageHoldingAsync(cancellationToken);
        var buy = await owner.RecordBrokerageTradeAsync(holding.AssetId, cancellationToken, feeAmount: 3m);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var record = await stranger.PostAsJsonAsync(
            TransactionsUri(holding.AssetId), NewBrokerageTradeRequest(), cancellationToken);
        var update = await stranger.PutAsJsonAsync(
            TransactionUri(holding.AssetId, buy.Id),
            new UpdateTransactionRequest { Type = TransactionType.Buy, Quantity = 1m, UnitPrice = 1m, Date = CashDay1 },
            cancellationToken);
        var delete = await stranger.DeleteAsync(TransactionUri(holding.AssetId, buy.Id), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, record.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal(3_797m, await owner.GetCashBalanceAsync(holding.AccountId, cancellationToken));
    }
}
