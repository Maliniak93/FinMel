using System.Net;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class DeleteTransferEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Delete_ManualTransfer_RemovesBothLegs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateCashAndSavingsAsync(cancellationToken);
        var transferId = await client.CreateTransferAsync(cancellationToken, NewTransferRequest(setup.CashAssetId, setup.SavingsAssetId, 2_000m));

        var response = await client.DeleteAsync(TransferUri(transferId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await client.AssertCashUntouchedAsync(setup.CashPortfolioId, setup.CashAssetId, cancellationToken);
        var savings = await client.GetAssetAsync(setup.SavingsPortfolioId, setup.SavingsAssetId, cancellationToken);
        Assert.Equal(0m, savings.Quantity);
        Assert.Equal(0, savings.TransactionCount);
        await using var dbContext = CreateDbContext(userId);
        Assert.False(await dbContext.Transactions.AnyAsync(t => t.TransferId != null, cancellationToken));
        Assert.Equal(1, await dbContext.Transactions.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task MetalBuyWithCash_RemovesBothLegs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateCashAndMetalAsync(cancellationToken);
        var buy = await client.RecordMetalTransactionWithCashAsync(setup.MetalPortfolioId, setup.MetalAssetId, setup.CashAssetId, cancellationToken);
        Assert.NotNull(buy.Transfer);

        var response = await client.DeleteAsync(TransferUri(buy.Transfer.TransferId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await client.AssertCashUntouchedAsync(setup.CashPortfolioId, setup.CashAssetId, cancellationToken);
        var metal = await client.GetAssetAsync(setup.MetalPortfolioId, setup.MetalAssetId, cancellationToken);
        Assert.Equal(0m, metal.Quantity);
        Assert.Equal(0, metal.TransactionCount);
        await using var dbContext = CreateDbContext(userId);
        Assert.False(await dbContext.Transactions.AnyAsync(t => t.TransferId != null, cancellationToken));
    }

    [Fact]
    public async Task MetalBuy_LaterSellNeedsIt_Conflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateCashAndMetalAsync(cancellationToken);
        var buy = await client.RecordMetalTransactionWithCashAsync(setup.MetalPortfolioId, setup.MetalAssetId, setup.CashAssetId, cancellationToken);
        Assert.NotNull(buy.Transfer);
        await client.RecordTransactionAsync(
            setup.MetalPortfolioId, setup.MetalAssetId, TransactionType.Sell, 1m, MetalCashDate.AddDays(1), cancellationToken, unitPrice: 1_300m);
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.DeleteAsync(TransferUri(buy.Transfer.TransferId), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.OversellsPositionErrorCode, cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
        Assert.Equal(1m, (await client.GetAssetAsync(setup.MetalPortfolioId, setup.MetalAssetId, cancellationToken)).Quantity);
        Assert.Equal(2_600m, (await client.GetAssetAsync(setup.CashPortfolioId, setup.CashAssetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task Delete_DepositRouteTransfer_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateFundedDepositAsync(cancellationToken);
        var cashLeg = await client.GetCashWithdrawAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken);
        Assert.NotNull(cashLeg.Transfer);

        var response = await client.DeleteAsync(TransferUri(cashLeg.Transfer.TransferId), cancellationToken);

        await response.AssertTransferLegManagedAsync(cancellationToken);
        await AssertFundedDepositUnchangedAsync(client, userId, funded, cancellationToken);
    }

    [Fact]
    public async Task Delete_WouldBreakTargetHistory_IsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateCashAndSavingsAsync(cancellationToken);
        var transferId = await client.CreateTransferAsync(cancellationToken, NewTransferRequest(setup.CashAssetId, setup.SavingsAssetId, 2_000m));
        await client.RecordTransactionAsync(
            setup.SavingsPortfolioId, setup.SavingsAssetId, TransactionType.Withdraw, 2_000m, new DateOnly(2026, 1, 25), cancellationToken, unitPrice: 1m);
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.DeleteAsync(TransferUri(transferId), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.OversellsPositionErrorCode, cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
        Assert.Equal(3_000m, (await client.GetAssetAsync(setup.CashPortfolioId, setup.CashAssetId, cancellationToken)).Quantity);
        await using var dbContext = CreateDbContext(userId);
        Assert.Equal(2, await dbContext.Transactions.CountAsync(t => t.TransferId == transferId, cancellationToken));
    }

    [Fact]
    public async Task Delete_UnknownTransfer_ReturnsNotFound()
    {
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.DeleteAsync(TransferUri(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("target")]
    public async Task Delete_ArchivedPortfolio_ReturnsConflict(string archivedSide)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateCashAndSavingsAsync(cancellationToken);
        var transferId = await client.CreateTransferAsync(cancellationToken, NewTransferRequest(setup.CashAssetId, setup.SavingsAssetId, 2_000m));
        await client.ArchivePortfolioAsync(archivedSide == "source" ? setup.CashPortfolioId : setup.SavingsPortfolioId, cancellationToken);
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.DeleteAsync(TransferUri(transferId), cancellationToken);

        await response.AssertPortfolioArchivedConflictAsync(cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
    }

    [Fact]
    public async Task Delete_DepositPayoutIntoSavings_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateDepositPaidIntoSavingsAsync(cancellationToken);
        var leg = Assert.Single((await client.ListTransactionsAsync(setup.SavingsPortfolioId, setup.Account.AssetId, cancellationToken)).Items);
        Assert.NotNull(leg.Transfer);

        var response = await client.DeleteAsync(TransferUri(leg.Transfer.TransferId), cancellationToken);

        await response.AssertTransferLegManagedAsync(cancellationToken);
        Assert.Equal(10_119.83m, (await client.GetAssetAsync(setup.SavingsPortfolioId, setup.Account.AssetId, cancellationToken)).Quantity);
        Assert.Equal(0m, (await client.GetAssetAsync(setup.DepositPortfolioId, setup.Deposit.AssetId, cancellationToken)).Quantity);
        await using var dbContext = CreateDbContext(userId);
        Assert.Equal(2, await dbContext.Transactions.CountAsync(t => t.TransferId == leg.Transfer.TransferId, cancellationToken));
    }
}
