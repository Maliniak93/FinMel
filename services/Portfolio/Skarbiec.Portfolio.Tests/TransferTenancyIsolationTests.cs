using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.UpdateTransaction;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class TransferTenancyIsolationTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task AddDeposit_FundedFromStrangersCash_ReturnsInvalidCounterpartAndWritesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var ownerCashId = await owner.AddCashAssetWithBalanceAsync(cancellationToken, balance: 5_000m);
        var strangerId = Guid.NewGuid();
        using var stranger = Factory.CreateAuthenticatedClient(strangerId);
        var ownerBefore = await SnapshotUserRowsAsync(ownerId, cancellationToken);
        var strangerBefore = await SnapshotUserRowsAsync(strangerId, cancellationToken);

        var response = await stranger.PostAsJsonAsync(
            AllDepositsUri, NewDepositRequest(principal: 1_000m, fundingAssetId: ownerCashId), cancellationToken);

        await response.AssertInvalidTransferCounterpartAsync(cancellationToken);
        Assert.Equal(ownerBefore, await SnapshotUserRowsAsync(ownerId, cancellationToken));
        Assert.Equal(strangerBefore, await SnapshotUserRowsAsync(strangerId, cancellationToken));
        await owner.AssertCashUntouchedAsync(ownerCashId, cancellationToken);
        await using var dbContext = CreateDbContext(ownerId);
        Assert.False(await dbContext.Transactions.IgnoreQueryFilters().AnyAsync(t => t.TransferId != null, cancellationToken));
    }

    [Fact]
    public async Task RecordMetalBuy_FundedFromStrangersCash_ReturnsInvalidCounterpartAndWritesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var ownerSetup = await owner.CreateCashAndMetalAsync(cancellationToken);
        var strangerId = Guid.NewGuid();
        using var stranger = Factory.CreateAuthenticatedClient(strangerId);
        var strangerSetup = await stranger.CreateCashAndMetalAsync(cancellationToken);
        var ownerBefore = await SnapshotUserRowsAsync(ownerId, cancellationToken);
        var strangerBefore = await SnapshotUserRowsAsync(strangerId, cancellationToken);

        var response = await stranger.PostAsJsonAsync(
            TransactionsUri(strangerSetup.MetalAssetId),
            NewMetalCashRequest(ownerSetup.CashAssetId),
            cancellationToken);

        await response.AssertInvalidTransferCounterpartAsync(cancellationToken);
        Assert.Equal(ownerBefore, await SnapshotUserRowsAsync(ownerId, cancellationToken));
        Assert.Equal(strangerBefore, await SnapshotUserRowsAsync(strangerId, cancellationToken));
        await owner.AssertCashUntouchedAsync(ownerSetup.CashAssetId, cancellationToken);
    }

    [Fact]
    public async Task SecurityTrade_ForeignCash_Rejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var ownerSetup = await CreateCashAndEtfAsync(owner, cancellationToken);
        var strangerId = Guid.NewGuid();
        using var stranger = Factory.CreateAuthenticatedClient(strangerId);
        var strangerSetup = await CreateCashAndEtfAsync(stranger, cancellationToken);
        var ownerBefore = await SnapshotUserRowsAsync(ownerId, cancellationToken);
        var strangerBefore = await SnapshotUserRowsAsync(strangerId, cancellationToken);

        var response = await stranger.PostAsJsonAsync(
            TransactionsUri(strangerSetup.EtfAssetId),
            NewTradeRequest(ownerSetup.CashAssetId),
            cancellationToken);

        await response.AssertInvalidTransferCounterpartAsync(cancellationToken);
        Assert.Equal(ownerBefore, await SnapshotUserRowsAsync(ownerId, cancellationToken));
        Assert.Equal(strangerBefore, await SnapshotUserRowsAsync(strangerId, cancellationToken));
        await owner.AssertCashUntouchedAsync(ownerSetup.CashAssetId, cancellationToken, balance: 2_000m);
    }

    [Fact]
    public async Task DeleteMetalTransfer_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var setup = await owner.CreateCashAndMetalAsync(cancellationToken);
        var buy = await owner.RecordMetalTransactionWithCashAsync(setup.MetalAssetId, setup.CashAssetId, cancellationToken);
        Assert.NotNull(buy.Transfer);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var before = await SnapshotUserRowsAsync(ownerId, cancellationToken);

        var response = await stranger.DeleteAsync(TransferUri(buy.Transfer.TransferId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, await SnapshotUserRowsAsync(ownerId, cancellationToken));
        Assert.Equal(2m, (await owner.GetAssetAsync(setup.MetalAssetId, cancellationToken)).Quantity);
        Assert.Equal(2_600m, (await owner.GetAssetAsync(setup.CashAssetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task TransferCandidates_ByStranger_NeverIncludeOwnersAssets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var ownerCashId = await owner.AddCashAssetWithBalanceAsync(cancellationToken, name: "Owner's cash");
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerCashId = await stranger.AddCashAssetWithBalanceAsync(cancellationToken, name: "Stranger's cash");

        var response = await stranger.GetAsync(TransferCandidatesUri("PLN", "Cash"), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var candidates = (await response.ReadJsonAsync(cancellationToken)).EnumerateArray().ToList();
        var own = Assert.Single(candidates);
        Assert.Equal(strangerCashId, own.GetProperty("assetId").GetGuid());
        Assert.DoesNotContain(candidates, c => c.GetProperty("assetId").GetGuid() == ownerCashId);
    }

    [Fact]
    public async Task AccountCash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var ownerAccount = await owner.CreateAccountWithCashAsync(cancellationToken, balance: 1_000m, name: "Owner");
        var ownerTarget = await owner.CreateAccountWithCashAsync(cancellationToken, name: "Owner target");
        var ownerTransferId = await owner.CreateTransferAsync(
            cancellationToken, NewTransferRequest(ownerAccount.CashAssetId, ownerTarget.CashAssetId, 400m, CashDay4));
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerAccount = await stranger.CreateAccountWithCashAsync(cancellationToken, balance: 1_000m, name: "Stranger");

        var transferFrom = await stranger.PostAsJsonAsync(
            TransfersUri,
            NewTransferRequest(ownerAccount.CashAssetId, strangerAccount.CashAssetId, 100m, CashDay4),
            cancellationToken);
        var transferInto = await stranger.PostAsJsonAsync(
            TransfersUri,
            NewTransferRequest(strangerAccount.CashAssetId, ownerAccount.CashAssetId, 100m, CashDay4),
            cancellationToken);
        var deleteTransfer = await stranger.DeleteAsync(TransferUri(ownerTransferId), cancellationToken);
        var candidates = await stranger.GetAsync(TransferCandidatesUri("PLN", "Cash", accountCash: true), cancellationToken);

        Assert.All(new[] { transferFrom, transferInto }, response =>
            Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest));
        Assert.Equal(HttpStatusCode.NotFound, deleteTransfer.StatusCode);
        Assert.Equal(600m, await owner.GetCashBalanceAsync(ownerAccount.AccountId, cancellationToken));
        Assert.Equal(400m, await owner.GetCashBalanceAsync(ownerTarget.AccountId, cancellationToken));
        Assert.Equal(1_000m, await stranger.GetCashBalanceAsync(strangerAccount.AccountId, cancellationToken));
        Assert.Equal(HttpStatusCode.OK, candidates.StatusCode);
        var listed = (await candidates.ReadJsonAsync(cancellationToken)).EnumerateArray()
            .Select(c => c.GetProperty("assetId").GetGuid()).ToList();
        Assert.DoesNotContain(ownerAccount.CashAssetId, listed);
        Assert.DoesNotContain(ownerTarget.CashAssetId, listed);
    }

    [Fact]
    public async Task UpdateTransferLeg_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var funded = await owner.CreateFundedDepositAsync(cancellationToken);
        var cashLeg = await owner.GetCashWithdrawAsync(funded.CashAssetId, cancellationToken);
        var depositLeg = Assert.Single((await owner.ListTransactionsAsync(funded.Deposit.AssetId, cancellationToken)).Items);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var update = new UpdateTransactionRequest { Type = TransactionType.Withdraw, Quantity = 1m, UnitPrice = 1m, Date = cashLeg.Date };

        HttpResponseMessage[] responses =
        [
            await stranger.PutAsJsonAsync(TransactionUri(funded.CashAssetId, cashLeg.Id), update, cancellationToken),
            await stranger.PutAsJsonAsync(TransactionUri(funded.CashAssetId, cashLeg.Id), update, cancellationToken),
            await stranger.PutAsJsonAsync(TransactionUri(funded.Deposit.AssetId, depositLeg.Id), update, cancellationToken),
            await stranger.PutAsJsonAsync(TransactionUri(funded.Deposit.AssetId, depositLeg.Id), update, cancellationToken),
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
        var cashLeg = await owner.GetCashWithdrawAsync(funded.CashAssetId, cancellationToken);
        var depositLeg = Assert.Single((await owner.ListTransactionsAsync(funded.Deposit.AssetId, cancellationToken)).Items);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        HttpResponseMessage[] responses =
        [
            await stranger.DeleteAsync(TransactionUri(funded.CashAssetId, cashLeg.Id), cancellationToken),
            await stranger.DeleteAsync(TransactionUri(funded.CashAssetId, cashLeg.Id), cancellationToken),
            await stranger.DeleteAsync(TransactionUri(funded.Deposit.AssetId, depositLeg.Id), cancellationToken),
            await stranger.DeleteAsync(TransactionUri(funded.Deposit.AssetId, depositLeg.Id), cancellationToken),
        ];

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        await AssertFundedDepositUnchangedAsync(owner, ownerId, funded, cancellationToken);
    }

    [Fact]
    public async Task ListTransferLegs_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var funded = await owner.CreateFundedDepositAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await stranger.GetAsync(TransactionsUri(funded.CashAssetId), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

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
        await owner.AssertCashUntouchedAsync(ownerSetup.CashAssetId, cancellationToken);
    }

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
        Assert.Equal(3_000m, (await owner.GetAssetAsync(setup.CashAssetId, cancellationToken)).Quantity);
        Assert.Equal(2_000m, (await owner.GetAssetAsync(setup.SavingsAssetId, cancellationToken)).Quantity);
    }
}
