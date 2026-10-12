using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Transfers;
using Skarbiec.Portfolio.Features.Transfers.CreateTransfer;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

// Transfer create and delete are under test, so they are posted directly; AccountsApi helpers only arrange.
public sealed class AccountTransferEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Create_BetweenAccounts_MovesCashAsTwoLinkedLegs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var a = await client.CreateAccountWithCashAsync(cancellationToken, balance: 5_000m, name: "A");
        var b = await client.CreateAccountWithCashAsync(cancellationToken, name: "B");

        var response = await client.PostAsJsonAsync(
            TransfersUri, NewTransferRequest(a.CashAssetId, b.CashAssetId, 3_000m, CashDay4), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2_000m, await client.GetCashBalanceAsync(a.AccountId, cancellationToken));
        Assert.Equal(3_000m, await client.GetCashBalanceAsync(b.AccountId, cancellationToken));
        var transferId = (await response.Content.ReadFromJsonAsync<CreateTransferResponse>(cancellationToken))!.TransferId;
        await using var dbContext = CreateDbContext(userId);
        var legs = await dbContext.Transactions.Where(t => t.TransferId == transferId).ToListAsync(cancellationToken);
        Assert.Equal(2, legs.Count);
        Assert.Contains(legs, t => t.AssetId == a.CashAssetId && t.Type == TransactionType.Withdraw && t.Quantity == 3_000m);
        Assert.Contains(legs, t => t.AssetId == b.CashAssetId && t.Type == TransactionType.Deposit && t.Quantity == 3_000m);
    }

    [Fact]
    public async Task List_BetweenAccounts_BothLegsAreManual()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var a = await client.CreateAccountWithCashAsync(cancellationToken, balance: 5_000m, name: "A");
        var b = await client.CreateAccountWithCashAsync(cancellationToken, name: "B");
        var transferId = await client.CreateTransferAsync(
            cancellationToken, NewTransferRequest(a.CashAssetId, b.CashAssetId, 3_000m, CashDay4));

        var outLeg = Assert.Single(
            (await client.ListTransactionsAsync(a.CashAssetId, cancellationToken)).Items, t => t.Transfer is not null);
        var inLeg = Assert.Single((await client.ListTransactionsAsync(b.CashAssetId, cancellationToken)).Items);

        Assert.Equal(transferId, outLeg.Transfer!.TransferId);
        Assert.Equal(TransferDirection.Out, outLeg.Transfer.Direction);
        Assert.True(outLeg.Transfer.Manual);
        Assert.Equal(transferId, inLeg.Transfer!.TransferId);
        Assert.Equal(TransferDirection.In, inLeg.Transfer.Direction);
        Assert.True(inLeg.Transfer.Manual);
    }

    [Fact]
    public async Task Create_MoreThanBalance_ReturnsInsufficientFunds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var a = await client.CreateAccountWithCashAsync(cancellationToken, balance: 5_000m, name: "A");
        var b = await client.CreateAccountWithCashAsync(cancellationToken, name: "B");

        var response = await client.PostAsJsonAsync(
            TransfersUri, NewTransferRequest(a.CashAssetId, b.CashAssetId, 6_000m, CashDay4), cancellationToken);

        await response.AssertInsufficientFundsAsync(cancellationToken);
        Assert.Equal(5_000m, await client.GetCashBalanceAsync(a.AccountId, cancellationToken));
        Assert.Equal(0m, await client.GetCashBalanceAsync(b.AccountId, cancellationToken));
    }

    [Fact]
    public async Task Create_ToFreeCashOrSameAccount_ReturnsInvalidCounterpart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var a = await client.CreateAccountWithCashAsync(cancellationToken, balance: 5_000m, name: "A");
        var freeCashId = await client.AddCashAssetWithBalanceAsync(cancellationToken, name: "Wallet");

        var toFree = await client.PostAsJsonAsync(
            TransfersUri, NewTransferRequest(a.CashAssetId, freeCashId, 1_000m, CashDay4), cancellationToken);
        var fromFree = await client.PostAsJsonAsync(
            TransfersUri, NewTransferRequest(freeCashId, a.CashAssetId, 1_000m, CashDay4), cancellationToken);
        var toSame = await client.PostAsJsonAsync(
            TransfersUri, NewTransferRequest(a.CashAssetId, a.CashAssetId, 1_000m, CashDay4), cancellationToken);

        await toFree.AssertInvalidTransferCounterpartAsync(cancellationToken);
        await fromFree.AssertInvalidTransferCounterpartAsync(cancellationToken);
        await toSame.AssertInvalidTransferCounterpartAsync(cancellationToken);
        Assert.Equal(5_000m, await client.GetCashBalanceAsync(a.AccountId, cancellationToken));
    }

    [Fact]
    public async Task Delete_AfterTargetSpentTheMoney_ReturnsInsufficientFunds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var a = await client.CreateAccountWithCashAsync(cancellationToken, balance: 5_000m, name: "A");
        var b = await client.CreateAccountWithCashAsync(cancellationToken, name: "B");
        var transferId = await client.CreateTransferAsync(
            cancellationToken, NewTransferRequest(a.CashAssetId, b.CashAssetId, 3_000m, CashDay4));
        await client.RecordTransactionAsync(
            b.CashAssetId, TransactionType.Withdraw, 1_000m, CashDay4.AddDays(1), cancellationToken, unitPrice: 1m);

        var response = await client.DeleteAsync(TransferUri(transferId), cancellationToken);

        await response.AssertInsufficientFundsAsync(cancellationToken);
        Assert.Equal(2_000m, await client.GetCashBalanceAsync(a.AccountId, cancellationToken));
        Assert.Equal(2_000m, await client.GetCashBalanceAsync(b.AccountId, cancellationToken));
    }

    [Fact]
    public async Task Delete_UntouchedTransfer_RestoresBothBalances()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var a = await client.CreateAccountWithCashAsync(cancellationToken, balance: 5_000m, name: "A");
        var b = await client.CreateAccountWithCashAsync(cancellationToken, name: "B");
        var transferId = await client.CreateTransferAsync(
            cancellationToken, NewTransferRequest(a.CashAssetId, b.CashAssetId, 3_000m, CashDay4));

        var response = await client.DeleteAsync(TransferUri(transferId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(5_000m, await client.GetCashBalanceAsync(a.AccountId, cancellationToken));
        Assert.Equal(0m, await client.GetCashBalanceAsync(b.AccountId, cancellationToken));
    }

    [Fact]
    public async Task Candidates_AccountCash_ListsAccountCashNotFreeCash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateAccountWithCashAsync(cancellationToken, name: "A");
        var b = await client.CreateAccountWithCashAsync(cancellationToken, balance: 700m, name: "B");
        var freeCashId = await client.AddCashAssetWithBalanceAsync(cancellationToken, name: "Wallet");

        var response = await client.GetAsync(TransferCandidatesUri("PLN", "Cash", accountCash: true), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var candidates = (await response.ReadJsonAsync(cancellationToken)).EnumerateArray().ToList();
        var listed = candidates.Select(c => c.GetProperty("assetId").GetGuid()).ToList();
        Assert.Contains(b.CashAssetId, listed);
        Assert.DoesNotContain(freeCashId, listed);
        Assert.Equal(700m, candidates.Single(c => c.GetProperty("assetId").GetGuid() == b.CashAssetId).GetProperty("balance").GetDecimal());
    }
}
