using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.UpdateTransaction;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

// The slice under test (record transaction) is called directly; fixture helpers only arrange.
public sealed class MetalCashTransactionEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Buy_WithCash_WritesLinkedWithdraw()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateCashAndMetalAsync(cancellationToken);

        var response = await client.PostAsJsonAsync(
            TransactionsUri(setup.MetalPortfolioId, setup.MetalAssetId), NewMetalCashRequest(setup.CashAssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2m, (await client.GetAssetAsync(setup.MetalPortfolioId, setup.MetalAssetId, cancellationToken)).Quantity);
        Assert.Equal(2_600m, (await client.GetAssetAsync(setup.CashPortfolioId, setup.CashAssetId, cancellationToken)).Quantity);
        await using var dbContext = CreateDbContext(userId);
        var legs = await dbContext.Transactions.Where(t => t.TransferId != null).ToListAsync(cancellationToken);
        Assert.Equal(2, legs.Count);
        Assert.Single(legs.Select(t => t.TransferId).Distinct());
        var buy = Assert.Single(legs, l => l.AssetId == setup.MetalAssetId);
        Assert.Equal(TransactionType.Buy, buy.Type);
        Assert.Equal(2m, buy.Quantity);
        Assert.Equal(1_200.00m, buy.UnitPriceAmount);
        var withdraw = Assert.Single(legs, l => l.AssetId == setup.CashAssetId);
        Assert.Equal(TransactionType.Withdraw, withdraw.Type);
        Assert.Equal(2_400.00m, withdraw.Quantity);
        Assert.Equal(1m, withdraw.UnitPriceAmount);
        Assert.Equal(MetalCashDate, withdraw.Date);
        Assert.Equal(1m, withdraw.FxRateToPln);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(setup.MetalPortfolioId, setup.MetalAssetId, cancellationToken);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(setup.CashPortfolioId, setup.CashAssetId, cancellationToken);
    }

    [Fact]
    public async Task Sell_WithCash_WritesLinkedDeposit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateCashAndMetalAsync(cancellationToken, pieces: 3m);

        var response = await client.PostAsJsonAsync(
            TransactionsUri(setup.MetalPortfolioId, setup.MetalAssetId),
            NewMetalCashRequest(setup.CashAssetId, TransactionType.Sell, pieces: 1m, pricePerPiece: 1_300.00m),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2m, (await client.GetAssetAsync(setup.MetalPortfolioId, setup.MetalAssetId, cancellationToken)).Quantity);
        Assert.Equal(6_300m, (await client.GetAssetAsync(setup.CashPortfolioId, setup.CashAssetId, cancellationToken)).Quantity);
        await using var dbContext = CreateDbContext(userId);
        var legs = await dbContext.Transactions.Where(t => t.TransferId != null).ToListAsync(cancellationToken);
        Assert.Equal(2, legs.Count);
        Assert.Single(legs.Select(t => t.TransferId).Distinct());
        Assert.Equal(TransactionType.Sell, Assert.Single(legs, l => l.AssetId == setup.MetalAssetId).Type);
        var deposit = Assert.Single(legs, l => l.AssetId == setup.CashAssetId);
        Assert.Equal(TransactionType.Deposit, deposit.Type);
        Assert.Equal(1_300.00m, deposit.Quantity);
        Assert.Equal(1m, deposit.UnitPriceAmount);
    }

    [Theory]
    [InlineData("savings")]
    [InlineData("eur-cash")]
    [InlineData("archived-cash")]
    [InlineData("holding-itself")]
    public async Task InvalidCounterpart_ReturnsBadRequest(string scenario)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateCashAndMetalAsync(cancellationToken);
        Guid cashAssetId;
        switch (scenario)
        {
            case "savings":
                cashAssetId = (await client.CreatePortfolioWithSavingsAccountAsync(
                    cancellationToken, NewSavingsAccountRequest(withOpeningDeposit: false))).Account.AssetId;
                break;
            case "eur-cash":
                cashAssetId = await client.AddCashAssetWithBalanceAsync(setup.CashPortfolioId, cancellationToken, currency: "EUR", name: "Euro cash");
                break;
            case "archived-cash":
                cashAssetId = (await client.AddArchivedCashAssetInLivePortfolioAsync(cancellationToken, portfolioName: "Old wallet")).CashId;
                break;
            default:
                cashAssetId = setup.MetalAssetId;
                break;
        }

        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.PostAsJsonAsync(
            TransactionsUri(setup.MetalPortfolioId, setup.MetalAssetId), NewMetalCashRequest(cashAssetId, pieces: 1m, pricePerPiece: 100m), cancellationToken);

        await response.AssertInvalidTransferCounterpartAsync(cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
        await using var dbContext = CreateDbContext(userId);
        Assert.False(await dbContext.Transactions.AnyAsync(t => t.TransferId != null, cancellationToken));
    }

    [Fact]
    public async Task InsufficientCash_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateCashAndMetalAsync(cancellationToken, cashBalance: 1_000m);
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.PostAsJsonAsync(
            TransactionsUri(setup.MetalPortfolioId, setup.MetalAssetId), NewMetalCashRequest(setup.CashAssetId), cancellationToken);

        await response.AssertInsufficientFundsAsync(cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
        await client.AssertCashUntouchedAsync(setup.CashPortfolioId, setup.CashAssetId, cancellationToken, balance: 1_000m);
    }

    [Fact]
    public async Task LinkedLegs_UpdateOrDelete_ReturnTransferLegManaged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateCashAndMetalAsync(cancellationToken);
        var buy = await client.RecordMetalTransactionWithCashAsync(setup.MetalPortfolioId, setup.MetalAssetId, setup.CashAssetId, cancellationToken);
        var withdraw = await client.GetCashWithdrawAsync(setup.CashPortfolioId, setup.CashAssetId, cancellationToken);
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);
        var updateBuy = new UpdateTransactionRequest { Type = TransactionType.Buy, Quantity = 1m, UnitPrice = 1_200m, Date = buy.Date };
        var updateWithdraw = new UpdateTransactionRequest { Type = TransactionType.Withdraw, Quantity = 1m, UnitPrice = 1m, Date = withdraw.Date };

        HttpResponseMessage[] responses =
        [
            await client.PutAsJsonAsync(TransactionUri(setup.MetalPortfolioId, setup.MetalAssetId, buy.Id), updateBuy, cancellationToken),
            await client.DeleteAsync(TransactionUri(setup.MetalPortfolioId, setup.MetalAssetId, buy.Id), cancellationToken),
            await client.PutAsJsonAsync(TransactionUri(setup.CashPortfolioId, setup.CashAssetId, withdraw.Id), updateWithdraw, cancellationToken),
            await client.DeleteAsync(TransactionUri(setup.CashPortfolioId, setup.CashAssetId, withdraw.Id), cancellationToken),
        ];

        foreach (var response in responses)
        {
            await response.AssertTransferLegManagedAsync(cancellationToken);
        }

        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
    }
}
