using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.UpdateTransaction;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

// The slices under test (record, update and delete transaction) are called directly; fixture helpers only arrange.
[Collection(TestingDefaults.CollectionName)]
public sealed class SecurityCashTradeEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Buy_FromCash_WritesLinkedLegs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.FxRateLookupClient.WithRate("EUR", 4.30m);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await CreateCashAndEtfAsync(client, cancellationToken);

        var response = await client.PostAsJsonAsync(
            TransactionsUri(setup.EtfPortfolioId, setup.EtfAssetId), NewTradeRequest(setup.CashAssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(15m, (await client.GetAssetAsync(setup.EtfPortfolioId, setup.EtfAssetId, cancellationToken)).Quantity);
        Assert.Equal(350m, (await client.GetAssetAsync(setup.CashPortfolioId, setup.CashAssetId, cancellationToken)).Quantity);
        await using var dbContext = CreateDbContext(userId);
        var legs = await dbContext.Transactions.Where(t => t.TransferId != null).ToListAsync(cancellationToken);
        Assert.Equal(2, legs.Count);
        Assert.Single(legs.Select(t => t.TransferId).Distinct());
        var buy = Assert.Single(legs, l => l.AssetId == setup.EtfAssetId);
        Assert.Equal(TransactionType.Buy, buy.Type);
        Assert.Equal(15m, buy.Quantity);
        Assert.Equal(110m, buy.UnitPriceAmount);
        var withdraw = Assert.Single(legs, l => l.AssetId == setup.CashAssetId);
        Assert.Equal(TransactionType.Withdraw, withdraw.Type);
        Assert.Equal(1_650.00m, withdraw.Quantity);
        Assert.Equal(1m, withdraw.UnitPriceAmount);
        Assert.Equal(TradeDate, withdraw.Date);
        Assert.Equal(4.30m, buy.FxRateToPln);
        Assert.Equal(buy.FxRateToPln, withdraw.FxRateToPln);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(setup.EtfPortfolioId, setup.EtfAssetId, cancellationToken);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(setup.CashPortfolioId, setup.CashAssetId, cancellationToken);
    }

    [Fact]
    public async Task SellAndDividend_IntoCash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await CreateCashAndEtfAsync(client, cancellationToken, etfQuantity: 20m);

        var sell = await client.PostAsJsonAsync(
            TransactionsUri(setup.EtfPortfolioId, setup.EtfAssetId),
            NewTradeRequest(setup.CashAssetId, TransactionType.Sell, quantity: 5m, unitPrice: 130m),
            cancellationToken);
        var dividend = await client.PostAsJsonAsync(
            TransactionsUri(setup.EtfPortfolioId, setup.EtfAssetId),
            NewTradeRequest(setup.CashAssetId, TransactionType.Dividend, quantity: 12.40m, unitPrice: 1m, date: TradeDate.AddDays(1)),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, sell.StatusCode);
        Assert.Equal(HttpStatusCode.Created, dividend.StatusCode);
        Assert.Equal(15m, (await client.GetAssetAsync(setup.EtfPortfolioId, setup.EtfAssetId, cancellationToken)).Quantity);
        Assert.Equal(2_662.40m, (await client.GetAssetAsync(setup.CashPortfolioId, setup.CashAssetId, cancellationToken)).Quantity);
        await using var dbContext = CreateDbContext(userId);
        var cashLegs = await dbContext.Transactions
            .Where(t => t.AssetId == setup.CashAssetId && t.TransferId != null)
            .ToListAsync(cancellationToken);
        Assert.Equal(2, cashLegs.Count);
        Assert.All(cashLegs, l => Assert.Equal(TransactionType.Deposit, l.Type));
        Assert.Contains(cashLegs, l => l.Quantity == 650.00m && l.Date == TradeDate);
        Assert.Contains(cashLegs, l => l.Quantity == 12.40m && l.Date == TradeDate.AddDays(1));
        var securityLegs = await dbContext.Transactions
            .Where(t => t.AssetId == setup.EtfAssetId && t.TransferId != null)
            .ToListAsync(cancellationToken);
        Assert.Equal(2, securityLegs.Count);
        Assert.Equal(cashLegs.Select(l => l.TransferId).Order(), securityLegs.Select(l => l.TransferId).Order());
    }

    [Fact]
    public async Task Buy_ExceedsBalance_Returns400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await CreateCashAndEtfAsync(client, cancellationToken, cashBalance: 1_000m);
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.PostAsJsonAsync(
            TransactionsUri(setup.EtfPortfolioId, setup.EtfAssetId), NewTradeRequest(setup.CashAssetId), cancellationToken);

        await response.AssertInsufficientFundsAsync(cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
        await client.AssertCashUntouchedAsync(setup.CashPortfolioId, setup.CashAssetId, cancellationToken, balance: 1_000m);
    }

    [Theory]
    [InlineData("pln-cash")]
    [InlineData("savings")]
    [InlineData("archived-cash")]
    [InlineData("foreign-cash")]
    public async Task InvalidCounterpart_Returns400(string scenario)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await CreateCashAndEtfAsync(client, cancellationToken);
        Guid cashAssetId;
        switch (scenario)
        {
            case "pln-cash":
                cashAssetId = await client.AddCashAssetWithBalanceAsync(setup.CashPortfolioId, cancellationToken, currency: "PLN", name: "Zloty cash");
                break;
            case "savings":
                cashAssetId = (await client.CreatePortfolioWithSavingsAccountAsync(
                    cancellationToken, NewSavingsAccountRequest(withOpeningDeposit: false))).Account.AssetId;
                break;
            case "archived-cash":
                cashAssetId = (await client.AddArchivedCashAssetInLivePortfolioAsync(
                    cancellationToken, portfolioName: "Old wallet", currency: "EUR")).CashId;
                break;
            default:
                using (var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid()))
                {
                    var strangerWalletId = await stranger.CreatePortfolioAsync(cancellationToken, name: "Wallet");
                    cashAssetId = await stranger.AddCashAssetWithBalanceAsync(strangerWalletId, cancellationToken, currency: "EUR");
                }

                break;
        }

        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.PostAsJsonAsync(
            TransactionsUri(setup.EtfPortfolioId, setup.EtfAssetId),
            NewTradeRequest(cashAssetId, quantity: 1m, unitPrice: 100m),
            cancellationToken);

        await response.AssertInvalidTransferCounterpartAsync(cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
        await using var dbContext = CreateDbContext(userId);
        Assert.False(await dbContext.Transactions.AnyAsync(t => t.TransferId != null, cancellationToken));
    }

    [Theory]
    [InlineData("deposit-type")]
    [InlineData("crypto")]
    public async Task LinkNotAllowed_Returns400(string scenario)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await CreateCashAndEtfAsync(client, cancellationToken);
        var portfolioId = setup.EtfPortfolioId;
        var assetId = setup.EtfAssetId;
        var request = NewTradeRequest(setup.CashAssetId, TransactionType.Deposit, quantity: 5m, unitPrice: 1m);
        if (scenario == "crypto")
        {
            assetId = await AddQuotedHoldingAsync(
                client, portfolioId, cancellationToken, AssetClass.Crypto, "EUR", name: "Bitcoin", ticker: "BTC-EUR");
            request = NewTradeRequest(setup.CashAssetId, quantity: 1m, unitPrice: 100m);
        }

        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.PostAsJsonAsync(TransactionsUri(portfolioId, assetId), request, cancellationToken);

        await response.AssertCashLinkNotAllowedAsync(cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
        await client.AssertCashUntouchedAsync(setup.CashPortfolioId, setup.CashAssetId, cancellationToken, balance: 2_000m);
    }

    [Fact]
    public async Task Update_RewritesCashLeg()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await CreateCashAndEtfAsync(client, cancellationToken);
        var buy = await client.RecordTradeWithCashAsync(setup.EtfPortfolioId, setup.EtfAssetId, setup.CashAssetId, cancellationToken);
        var update = new UpdateTransactionRequest
        {
            Type = TransactionType.Buy,
            Quantity = 10m,
            UnitPrice = 110m,
            Date = TradeDate.AddDays(2)
        };

        var response = await client.PutAsJsonAsync(
            TransactionUri(setup.EtfPortfolioId, setup.EtfAssetId, buy.Id), update, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(10m, (await client.GetAssetAsync(setup.EtfPortfolioId, setup.EtfAssetId, cancellationToken)).Quantity);
        Assert.Equal(900m, (await client.GetAssetAsync(setup.CashPortfolioId, setup.CashAssetId, cancellationToken)).Quantity);
        await using var dbContext = CreateDbContext(userId);
        var legs = await dbContext.Transactions.Where(t => t.TransferId != null).ToListAsync(cancellationToken);
        Assert.Equal(2, legs.Count);
        Assert.Single(legs.Select(t => t.TransferId).Distinct());
        var withdraw = Assert.Single(legs, l => l.AssetId == setup.CashAssetId);
        Assert.Equal(TransactionType.Withdraw, withdraw.Type);
        Assert.Equal(1_100.00m, withdraw.Quantity);
        Assert.Equal(TradeDate.AddDays(2), withdraw.Date);
    }

    [Fact]
    public async Task Update_TypeChange_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await CreateCashAndEtfAsync(client, cancellationToken);
        var buy = await client.RecordTradeWithCashAsync(setup.EtfPortfolioId, setup.EtfAssetId, setup.CashAssetId, cancellationToken);
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);
        var update = new UpdateTransactionRequest { Type = TransactionType.Sell, Quantity = 15m, UnitPrice = 110m, Date = TradeDate };

        var response = await client.PutAsJsonAsync(
            TransactionUri(setup.EtfPortfolioId, setup.EtfAssetId, buy.Id), update, cancellationToken);

        await response.AssertTransferLegManagedAsync(cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
        Assert.Equal(350m, (await client.GetAssetAsync(setup.CashPortfolioId, setup.CashAssetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task Delete_RemovesBothLegs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await CreateCashAndEtfAsync(client, cancellationToken);
        var buy = await client.RecordTradeWithCashAsync(setup.EtfPortfolioId, setup.EtfAssetId, setup.CashAssetId, cancellationToken);

        var response = await client.DeleteAsync(TransactionUri(setup.EtfPortfolioId, setup.EtfAssetId, buy.Id), cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Delete answered {(int)response.StatusCode}.");
        Assert.Equal(0m, (await client.GetAssetAsync(setup.EtfPortfolioId, setup.EtfAssetId, cancellationToken)).Quantity);
        await client.AssertCashUntouchedAsync(setup.CashPortfolioId, setup.CashAssetId, cancellationToken, balance: 2_000m);
        await using var dbContext = CreateDbContext(userId);
        Assert.False(await dbContext.Transactions.AnyAsync(t => t.TransferId != null, cancellationToken));
        Assert.Empty(await dbContext.Transactions.Where(t => t.AssetId == setup.EtfAssetId).ToListAsync(cancellationToken));
    }

    [Fact]
    public async Task Delete_BreaksCashHistory_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await CreateCashAndEtfAsync(client, cancellationToken, etfQuantity: 20m);
        var sell = await client.RecordTradeWithCashAsync(
            setup.EtfPortfolioId, setup.EtfAssetId, setup.CashAssetId, cancellationToken,
            type: TransactionType.Sell, quantity: 5m, unitPrice: 130m);
        await client.RecordTransactionAsync(
            setup.CashPortfolioId, setup.CashAssetId, TransactionType.Withdraw, 2_650m, TradeDate.AddDays(2), cancellationToken, unitPrice: 1m);
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.DeleteAsync(TransactionUri(setup.EtfPortfolioId, setup.EtfAssetId, sell.Id), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.OversellsPositionErrorCode, cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
        Assert.Equal(15m, (await client.GetAssetAsync(setup.EtfPortfolioId, setup.EtfAssetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task CashLeg_IsManaged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await CreateCashAndEtfAsync(client, cancellationToken);
        await client.RecordTradeWithCashAsync(setup.EtfPortfolioId, setup.EtfAssetId, setup.CashAssetId, cancellationToken);
        var withdraw = await client.GetCashWithdrawAsync(setup.CashPortfolioId, setup.CashAssetId, cancellationToken);
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);
        var update = new UpdateTransactionRequest { Type = TransactionType.Withdraw, Quantity = 1m, UnitPrice = 1m, Date = withdraw.Date };

        HttpResponseMessage[] responses =
        [
            await client.PutAsJsonAsync(TransactionUri(setup.CashPortfolioId, setup.CashAssetId, withdraw.Id), update, cancellationToken),
            await client.DeleteAsync(TransactionUri(setup.CashPortfolioId, setup.CashAssetId, withdraw.Id), cancellationToken),
        ];

        foreach (var response in responses)
        {
            await response.AssertTransferLegManagedAsync(cancellationToken);
        }

        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
    }
}
