using System.Net;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

/// <summary>
/// savings-cash-transfers: <c>DELETE /api/portfolio/transfers/{transferId}</c> removes both legs of a
/// manual transfer and recomputes both assets (the position events are proven by
/// <see cref="PortfolioOutboxTests.DeleteTransfer_PublishesPositionChangedForBothAssets"/>).
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class DeleteTransferEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    /// <summary>AC-4.</summary>
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

    /// <summary>AC-4: a funded deposit's transfer belongs to the deposit slices.</summary>
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

    /// <summary>AC-4: the savings money was withdrawn after the transfer, so removing the inflow would leave a Withdraw uncovered.</summary>
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

    /// <summary>An archived portfolio on either side is a 409 and the transfer stays.</summary>
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
}
