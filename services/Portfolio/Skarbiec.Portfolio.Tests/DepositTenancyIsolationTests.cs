using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class DepositTenancyIsolationTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task List_ByStranger_DoesNotIncludeOwnersDeposit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await owner.AddDepositAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await stranger.AddDepositAsync(cancellationToken, NewDepositRequest(name: "Stranger's own"));

        var response = await stranger.GetAsync(AllDepositsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var listed = Assert.Single((await response.Content.ReadFromJsonAsync<List<DepositResponse>>(cancellationToken))!);
        Assert.Equal("Stranger's own", listed.Name);
    }

    [Fact]
    public async Task Get_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var deposit = await owner.AddDepositAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await stranger.GetAsync(DepositUri(deposit.AssetId), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_ByStranger_ReturnsNotFoundAndLeavesDeposit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var deposit = await owner.AddDepositAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var payload = NewDepositRequest(name: "Stranger's edit", principal: 1m).ToUpdateRequest();

        var response = await stranger.PutAsJsonAsync(DepositUri(deposit.AssetId), payload, cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var asset = await owner.GetAssetAsync(deposit.AssetId, cancellationToken);
        Assert.Equal("Term deposit", asset.Name);
        Assert.Equal(10_000m, asset.Quantity);
        await using var dbContext = CreateDbContext(ownerId);
        Assert.Equal(10_000m, (await dbContext.Set<TermDeposit>().SingleAsync(t => t.AssetId == deposit.AssetId, cancellationToken)).Principal);
    }

    [Fact]
    public async Task Delete_ByStranger_ReturnsNotFoundAndLeavesDeposit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var deposit = await owner.AddDepositAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await stranger.DeleteAsync(AssetUri(deposit.AssetId), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var stillThere = await owner.GetAsync(DepositUri(deposit.AssetId), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
        await using var dbContext = CreateDbContext(ownerId);
        Assert.True(await dbContext.Set<TermDeposit>().AnyAsync(t => t.AssetId == deposit.AssetId, cancellationToken));
        Assert.Equal(1, await dbContext.Transactions.CountAsync(t => t.AssetId == deposit.AssetId, cancellationToken));
    }

    [Fact]
    public async Task Settle_ForeignDeposit_IsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var deposit = await owner.AddDepositAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var previewViaOwnersAccount = await stranger.GetAsync(
            DepositSettlementPreviewUri(deposit.AssetId), cancellationToken);
        var previewViaStrangersAccount = await stranger.GetAsync(
            DepositSettlementPreviewUri(deposit.AssetId), cancellationToken);
        var settleViaOwnersAccount = await stranger.PostAsJsonAsync(
            SettleDepositUri(deposit.AssetId), NewSettleRequest(), cancellationToken);
        var settleViaStrangersAccount = await stranger.PostAsJsonAsync(
            SettleDepositUri(deposit.AssetId), NewSettleRequest(), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, previewViaOwnersAccount.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, previewViaStrangersAccount.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, settleViaOwnersAccount.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, settleViaStrangersAccount.StatusCode);
        await owner.AssertDepositUnsettledAsync(deposit.AssetId, cancellationToken);
        await using var dbContext = CreateDbContext(ownerId);
        var terms = await dbContext.Set<TermDeposit>().SingleAsync(t => t.AssetId == deposit.AssetId, cancellationToken);
        Assert.Null(terms.SettledOn);
        Assert.Null(terms.SettledGrossInterest);
        Assert.Null(terms.SettledTax);
        // Control: the owner can still settle it — the 404s were about the caller, not the deposit.
        var ownSettle = await owner.PostAsJsonAsync(SettleDepositUri(deposit.AssetId), NewSettleRequest(), cancellationToken);
        Assert.True(ownSettle.IsSuccessStatusCode, $"The owner's settle answered {(int)ownSettle.StatusCode}.");
    }

    [Fact]
    public async Task RollOver_ForeignDeposit_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var deposit = await owner.AddDepositAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var before = await SnapshotDepositAsync(owner, ownerId, deposit.AssetId, cancellationToken);

        var response = await stranger.PostAsJsonAsync(
            RollOverDepositUri(deposit.AssetId), NewRollOverRequest(), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertDepositUnchangedAsync(owner, ownerId, deposit.AssetId, before, cancellationToken);
        await using (var dbContext = CreateDbContext(ownerId))
        {
            var terms = await dbContext.Set<TermDeposit>().SingleAsync(t => t.AssetId == deposit.AssetId, cancellationToken);
            Assert.Equal(0, terms.RolloverCount);
            Assert.Equal(10_000m, terms.Principal);
        }

        // Control: the owner can still roll it over — the 404s were about the caller, not the deposit.
        var ownRollOver = await owner.PostAsJsonAsync(
            RollOverDepositUri(deposit.AssetId), NewRollOverRequest(), cancellationToken);
        Assert.True(ownRollOver.IsSuccessStatusCode, $"The owner's roll over answered {(int)ownRollOver.StatusCode}.");
    }

    [Fact]
    public async Task TermDeposit_QueriedAsStranger_IsFilteredOut()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var deposit = await owner.AddDepositAsync(cancellationToken);

        await using var strangerDb = CreateDbContext(Guid.NewGuid());
        Assert.False(await strangerDb.Set<TermDeposit>().AnyAsync(t => t.AssetId == deposit.AssetId, cancellationToken));
        await using var ownerDb = CreateDbContext(ownerId);
        Assert.True(await ownerDb.Set<TermDeposit>().AnyAsync(t => t.AssetId == deposit.AssetId, cancellationToken));
    }

    [Fact]
    public async Task PayOut_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var deposit = await owner.AddDepositAsync(cancellationToken);
        await owner.SettleDepositAsync(deposit.AssetId, cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await stranger.PostAsJsonAsync(
            PayOutDepositUri(deposit.AssetId), NewPayOutRequest(), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var unchanged = await owner.GetDepositAsync(deposit.AssetId, cancellationToken);
        Assert.Equal(DepositStatus.Settled, unchanged.Status);
        Assert.Null(unchanged.PaidOutOn);
        Assert.Equal(10_119.83m, (await owner.GetAssetAsync(deposit.AssetId, cancellationToken)).Quantity);
        await using (var dbContext = CreateDbContext(ownerId))
        {
            Assert.False(await dbContext.Transactions.IgnoreQueryFilters().AnyAsync(
                t => t.AssetId == deposit.AssetId && t.Type == TransactionType.Withdraw, cancellationToken));
        }

        var ownPayOut = await owner.PostAsJsonAsync(
            PayOutDepositUri(deposit.AssetId), NewPayOutRequest(), cancellationToken);
        Assert.True(ownPayOut.IsSuccessStatusCode, $"The owner's payout answered {(int)ownPayOut.StatusCode}.");
    }

    [Fact]
    public async Task BankAccounts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var ownersBank = await owner.CreateBankAccountAsync(
            cancellationToken, "mBank", balance: 20_000m, depositedOn: DefaultTopUpDate);
        var ownersOtherBank = await owner.CreateBankAccountAsync(cancellationToken, "Nest", SavingsVariant);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangersBank = await stranger.CreateBankAccountAsync(
            cancellationToken, "Stranger bank", balance: 5_000m, depositedOn: DefaultTopUpDate);
        var before = await SnapshotUserRowsAsync(ownerId, cancellationToken);

        var fromOwnersAccount = await stranger.PostAsJsonAsync(
            AllDepositsUri, NewDepositRequest(sourceAccountId: ownersBank.AccountId), cancellationToken);
        var returningToOwnersAccount = await stranger.PostAsJsonAsync(
            AllDepositsUri,
            NewDepositRequest(sourceAccountId: strangersBank.AccountId, returnAccountId: ownersBank.AccountId),
            cancellationToken);
        var ownerReturningToStrangersAccount = await owner.PostAsJsonAsync(
            AllDepositsUri,
            NewDepositRequest(sourceAccountId: ownersBank.AccountId, returnAccountId: strangersBank.AccountId),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, fromOwnersAccount.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, returningToOwnersAccount.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ownerReturningToStrangersAccount.StatusCode);
        Assert.Equal(20_000m, await owner.GetCashBalanceAsync(ownersBank.AccountId, cancellationToken));
        Assert.Equal(5_000m, await stranger.GetCashBalanceAsync(strangersBank.AccountId, cancellationToken));
        Assert.Equal(before, await SnapshotUserRowsAsync(ownerId, cancellationToken));
        Assert.Empty((await stranger.GetFromJsonAsync<List<DepositResponse>>(AllDepositsUri, cancellationToken))!);

        var strangerListsOwnersAccount = await stranger.GetAsync(DepositsOfAccountUri(ownersBank.AccountId), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, strangerListsOwnersAccount.StatusCode);
        Assert.Empty((await strangerListsOwnersAccount.Content.ReadFromJsonAsync<List<DepositResponse>>(cancellationToken))!);

        // Control: the owner can still open a deposit from and into their own accounts.
        var own = await owner.PostAsJsonAsync(
            AllDepositsUri,
            NewDepositRequest(sourceAccountId: ownersBank.AccountId, returnAccountId: ownersOtherBank.AccountId),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, own.StatusCode);
    }
}
