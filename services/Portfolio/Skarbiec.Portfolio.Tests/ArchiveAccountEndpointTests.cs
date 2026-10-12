using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.RecordTransaction;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

// Archive is under test, so these facts post to it directly; the writes into an archived account are asserted raw too.
public sealed class ArchiveAccountEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Archive_WithHeldStock_ReturnsAccountNotEmpty()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);
        await client.AddStockInAccountAsync(accountId, cancellationToken, quantity: 10m);

        var response = await client.PostAsync(ArchiveAccountUri(accountId), content: null, cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.AccountNotEmptyErrorCode, cancellationToken);
        Assert.False((await client.GetAccountAsync(accountId, cancellationToken)).IsArchived);
    }

    [Fact]
    public async Task Archive_AfterSellingToZero_ArchivesAndBlocksWrites()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);
        var stockId = await client.AddStockInAccountAsync(accountId, cancellationToken, quantity: 10m);
        await client.RecordTransactionAsync(stockId, TransactionType.Sell, 10m, DefaultTopUpDate.AddDays(1), cancellationToken);
        await client.RecordTransactionAsync(
            await client.GetAccountCashAssetIdAsync(accountId, cancellationToken),
            TransactionType.Withdraw,
            100m,
            DefaultTopUpDate.AddDays(2),
            cancellationToken,
            unitPrice: 1m);

        var response = await client.PostAsync(ArchiveAccountUri(accountId), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AccountResponse>(cancellationToken);
        Assert.True(body!.IsArchived);

        var record = await client.PostAsJsonAsync(
            TransactionsUri(stockId),
            new RecordTransactionRequest { Type = TransactionType.Buy, Quantity = 1m, UnitPrice = 10m, Date = DefaultTopUpDate.AddDays(2) },
            cancellationToken);
        await record.AssertAccountArchivedConflictAsync(cancellationToken);

        var addAsset = await client.PostAsJsonAsync(
            AllAssetsUri,
            new AddAssetRequest
            {
                AccountId = accountId,
                AssetClass = AssetClass.Stock,
                Name = "Another stock",
                Currency = "PLN",
                InstrumentId = FakeInstrumentLookupClient.IdFor(AssetClass.Stock, "PLN")
            },
            cancellationToken);
        await addAsset.AssertAccountArchivedConflictAsync(cancellationToken);
    }

    [Fact]
    public async Task NeedsZeroCash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateAccountWithCashAsync(cancellationToken, balance: 100m, name: "A");
        var other = await client.CreateAccountWithCashAsync(cancellationToken, balance: 50m, name: "B");

        var blocked = await client.PostAsync(ArchiveAccountUri(account.AccountId), content: null, cancellationToken);

        await blocked.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.AccountNotEmptyErrorCode, cancellationToken);
        Assert.False((await client.GetAccountAsync(account.AccountId, cancellationToken)).IsArchived);

        await client.RecordTransactionAsync(
            account.CashAssetId, TransactionType.Withdraw, 100m, CashDay2, cancellationToken, unitPrice: 1m);
        var archived = await client.PostAsync(ArchiveAccountUri(account.AccountId), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        var deposit = await client.PostCashTransactionAsync(
            account.CashAssetId, TransactionType.Deposit, 10m, CashDay3, cancellationToken);
        var transferIn = await client.PostAsJsonAsync(
            TransfersUri, NewTransferRequest(other.CashAssetId, account.CashAssetId, 10m, CashDay3), cancellationToken);
        await deposit.AssertAccountArchivedConflictAsync(cancellationToken);
        await transferIn.AssertAccountArchivedConflictAsync(cancellationToken);
        Assert.Equal(50m, await client.GetCashBalanceAsync(other.AccountId, cancellationToken));
    }

    [Fact]
    public async Task Archive_AlreadyArchivedAccount_IsIdempotent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);
        await client.ArchiveAccountAsync(accountId, cancellationToken);

        var response = await client.PostAsync(ArchiveAccountUri(accountId), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<AccountResponse>(cancellationToken))!.IsArchived);
    }

    [Fact]
    public async Task Archive_NonExistentAccount_ReturnsNotFound()
    {
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PostAsync(
            ArchiveAccountUri(Guid.NewGuid()), content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
