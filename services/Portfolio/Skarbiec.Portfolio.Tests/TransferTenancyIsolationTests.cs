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

/// <summary>
/// asset-transfers-deposit-funding AC-10: a transfer links two of one user's transactions, so a
/// stranger can neither use the owner's Cash as a funding source (400
/// <c>Validation.InvalidTransferCounterpart</c> — the same answer as any id not in their pick list,
/// so nothing leaks), nor see it among their candidates, nor reach the owner's legs (404 via the
/// owner's portfolio id and via their own). Nested resources, so the facts are written by hand.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class TransferTenancyIsolationTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task AddDeposit_FundedFromStrangersCash_ReturnsInvalidCounterpartAndWritesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var ownerWalletId = await owner.CreatePortfolioAsync(cancellationToken, name: "Wallet");
        var ownerCashId = await owner.AddCashAssetWithBalanceAsync(ownerWalletId, cancellationToken, balance: 5_000m);
        var strangerId = Guid.NewGuid();
        using var stranger = Factory.CreateAuthenticatedClient(strangerId);
        var strangerSavingsId = await stranger.CreatePortfolioAsync(cancellationToken, name: "Savings");
        var ownerBefore = await SnapshotUserRowsAsync(ownerId, cancellationToken);
        var strangerBefore = await SnapshotUserRowsAsync(strangerId, cancellationToken);

        var response = await stranger.PostAsJsonAsync(
            DepositsUri(strangerSavingsId), NewDepositRequest(principal: 1_000m, fundingAssetId: ownerCashId), cancellationToken);

        await response.AssertInvalidTransferCounterpartAsync(cancellationToken);
        Assert.Equal(ownerBefore, await SnapshotUserRowsAsync(ownerId, cancellationToken));
        Assert.Equal(strangerBefore, await SnapshotUserRowsAsync(strangerId, cancellationToken));
        await owner.AssertCashUntouchedAsync(ownerWalletId, ownerCashId, cancellationToken);
        await using var dbContext = CreateDbContext(ownerId);
        Assert.False(await dbContext.Transactions.IgnoreQueryFilters().AnyAsync(t => t.TransferId != null, cancellationToken));
    }

    [Fact]
    public async Task TransferCandidates_ByStranger_NeverIncludeOwnersAssets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var ownerWalletId = await owner.CreatePortfolioAsync(cancellationToken, name: "Wallet");
        var ownerCashId = await owner.AddCashAssetWithBalanceAsync(ownerWalletId, cancellationToken, name: "Owner's cash");
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerWalletId = await stranger.CreatePortfolioAsync(cancellationToken, name: "Wallet");
        var strangerCashId = await stranger.AddCashAssetWithBalanceAsync(strangerWalletId, cancellationToken, name: "Stranger's cash");

        var response = await stranger.GetAsync(TransferCandidatesUri("PLN", "Cash"), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var candidates = (await response.ReadJsonAsync(cancellationToken)).EnumerateArray().ToList();
        var own = Assert.Single(candidates);
        Assert.Equal(strangerCashId, own.GetProperty("assetId").GetGuid());
        Assert.DoesNotContain(candidates, c => c.GetProperty("assetId").GetGuid() == ownerCashId);
    }

    [Fact]
    public async Task UpdateTransferLeg_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var funded = await owner.CreateFundedDepositAsync(cancellationToken);
        var cashLeg = await owner.GetCashWithdrawAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken);
        var depositLeg = Assert.Single((await owner.ListTransactionsAsync(funded.DepositPortfolioId, funded.Deposit.AssetId, cancellationToken)).Items);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerPortfolioId = await stranger.CreatePortfolioAsync(cancellationToken, name: "Stranger's");
        var update = new UpdateTransactionRequest { Type = TransactionType.Withdraw, Quantity = 1m, UnitPrice = 1m, Date = cashLeg.Date };

        HttpResponseMessage[] responses =
        [
            await stranger.PutAsJsonAsync(TransactionUri(funded.CashPortfolioId, funded.CashAssetId, cashLeg.Id), update, cancellationToken),
            await stranger.PutAsJsonAsync(TransactionUri(strangerPortfolioId, funded.CashAssetId, cashLeg.Id), update, cancellationToken),
            await stranger.PutAsJsonAsync(TransactionUri(funded.DepositPortfolioId, funded.Deposit.AssetId, depositLeg.Id), update, cancellationToken),
            await stranger.PutAsJsonAsync(TransactionUri(strangerPortfolioId, funded.Deposit.AssetId, depositLeg.Id), update, cancellationToken),
        ];

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        await AssertFundedDepositUnchangedAsync(owner, ownerId, funded, cancellationToken);
    }

    [Fact]
    public async Task DeleteTransferLeg_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var funded = await owner.CreateFundedDepositAsync(cancellationToken);
        var cashLeg = await owner.GetCashWithdrawAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken);
        var depositLeg = Assert.Single((await owner.ListTransactionsAsync(funded.DepositPortfolioId, funded.Deposit.AssetId, cancellationToken)).Items);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerPortfolioId = await stranger.CreatePortfolioAsync(cancellationToken, name: "Stranger's");

        HttpResponseMessage[] responses =
        [
            await stranger.DeleteAsync(TransactionUri(funded.CashPortfolioId, funded.CashAssetId, cashLeg.Id), cancellationToken),
            await stranger.DeleteAsync(TransactionUri(strangerPortfolioId, funded.CashAssetId, cashLeg.Id), cancellationToken),
            await stranger.DeleteAsync(TransactionUri(funded.DepositPortfolioId, funded.Deposit.AssetId, depositLeg.Id), cancellationToken),
            await stranger.DeleteAsync(TransactionUri(strangerPortfolioId, funded.Deposit.AssetId, depositLeg.Id), cancellationToken),
        ];

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        await AssertFundedDepositUnchangedAsync(owner, ownerId, funded, cancellationToken);
    }

    /// <summary>A stranger listing the owner's Cash transactions — the leg and its counterpart names included — gets a 404.</summary>
    [Fact]
    public async Task ListTransferLegs_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var funded = await owner.CreateFundedDepositAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerPortfolioId = await stranger.CreatePortfolioAsync(cancellationToken, name: "Stranger's");

        var viaOwnersPortfolio = await stranger.GetAsync(TransactionsUri(funded.CashPortfolioId, funded.CashAssetId), cancellationToken);
        var viaStrangersPortfolio = await stranger.GetAsync(TransactionsUri(strangerPortfolioId, funded.CashAssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, viaOwnersPortfolio.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, viaStrangersPortfolio.StatusCode);
    }

    /// <summary>savings-cash-transfers AC-6: a stranger moving money from or to the owner's asset is a 400 and nothing is written for either user.</summary>
    [Fact]
    public async Task Create_ForeignAsset_IsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var ownerSetup = await owner.CreateCashAndSavingsAsync(cancellationToken);
        var strangerId = Guid.NewGuid();
        using var stranger = Factory.CreateAuthenticatedClient(strangerId);
        var strangerSetup = await stranger.CreateCashAndSavingsAsync(cancellationToken);
        var ownerBefore = await SnapshotUserRowsAsync(ownerId, cancellationToken);
        var strangerBefore = await SnapshotUserRowsAsync(strangerId, cancellationToken);

        var fromOwnersCash = await stranger.PostAsJsonAsync(
            TransfersUri, NewTransferRequest(ownerSetup.CashAssetId, strangerSetup.SavingsAssetId), cancellationToken);
        var toOwnersSavings = await stranger.PostAsJsonAsync(
            TransfersUri, NewTransferRequest(strangerSetup.CashAssetId, ownerSetup.SavingsAssetId), cancellationToken);

        await fromOwnersCash.AssertInvalidTransferCounterpartAsync(cancellationToken);
        await toOwnersSavings.AssertInvalidTransferCounterpartAsync(cancellationToken);
        Assert.Equal(ownerBefore, await SnapshotUserRowsAsync(ownerId, cancellationToken));
        Assert.Equal(strangerBefore, await SnapshotUserRowsAsync(strangerId, cancellationToken));
        await owner.AssertCashUntouchedAsync(ownerSetup.CashPortfolioId, ownerSetup.CashAssetId, cancellationToken);
    }

    /// <summary>savings-cash-transfers AC-6: a stranger deleting the owner's transfer is a 404 and both sides stay as they were.</summary>
    [Fact]
    public async Task Delete_ForeignTransfer_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var setup = await owner.CreateCashAndSavingsAsync(cancellationToken);
        var transferId = await owner.CreateTransferAsync(cancellationToken, NewTransferRequest(setup.CashAssetId, setup.SavingsAssetId, 2_000m));
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var before = await SnapshotUserRowsAsync(ownerId, cancellationToken);

        var response = await stranger.DeleteAsync(TransferUri(transferId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, await SnapshotUserRowsAsync(ownerId, cancellationToken));
        Assert.Equal(3_000m, (await owner.GetAssetAsync(setup.CashPortfolioId, setup.CashAssetId, cancellationToken)).Quantity);
        Assert.Equal(2_000m, (await owner.GetAssetAsync(setup.SavingsPortfolioId, setup.SavingsAssetId, cancellationToken)).Quantity);
    }
}
