using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Accounts.UpdateAccount;
using Skarbiec.Portfolio.Features.UpdateAsset;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

// Cash writes under test are posted directly; the AccountsApi arrange helpers assert success.
public sealed class AccountCashEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task CashIsManaged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (_, cashId) = await client.CreateAccountWithCashAsync(cancellationToken);

        var update = await client.PutAsJsonAsync(
            AssetUri(cashId),
            new UpdateAssetRequest { AssetClass = AssetClass.Cash, Name = "Renamed", Currency = "PLN" },
            cancellationToken);
        var remove = await client.DeleteAsync(AssetUri(cashId), cancellationToken);
        var archive = await client.PostAsync(ArchiveAssetUri(cashId), content: null, cancellationToken);
        var restore = await client.PostAsync(RestoreAssetUri(cashId), content: null, cancellationToken);

        await update.AssertAccountCashManagedAsync(cancellationToken);
        await remove.AssertAccountCashManagedAsync(cancellationToken);
        await archive.AssertAccountCashManagedAsync(cancellationToken);
        await restore.AssertAccountCashManagedAsync(cancellationToken);
        Assert.Equal("XTB", (await client.GetAssetAsync(cashId, cancellationToken)).Name);
    }

    [Fact]
    public async Task Rename_RenamesAccountCash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, cashId) = await client.CreateAccountWithCashAsync(cancellationToken);

        var response = await client.PutAsJsonAsync(
            AccountUri(accountId), new UpdateAccountRequest { Name = "XTB IKE" }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("XTB IKE", (await client.GetAssetAsync(cashId, cancellationToken)).Name);
    }

    [Fact]
    public async Task DepositAndWithdraw_UpdateBalance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, cashId) = await client.CreateAccountWithCashAsync(cancellationToken);

        var deposit = await client.PostCashTransactionAsync(cashId, TransactionType.Deposit, 5_000m, CashDay1, cancellationToken);
        var withdraw = await client.PostCashTransactionAsync(cashId, TransactionType.Withdraw, 1_000m, CashDay2, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, deposit.StatusCode);
        Assert.Equal(HttpStatusCode.Created, withdraw.StatusCode);
        Assert.Equal(4_000m, await client.GetCashBalanceAsync(accountId, cancellationToken));
        Assert.Equal(4_000m, (await client.GetAssetAsync(cashId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task NegativeOnAnyDay_Returns400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, cashId) = await client.CreateAccountWithCashAsync(cancellationToken);
        var depositId = await client.RecordTransactionAsync(
            cashId, TransactionType.Deposit, 5_000m, CashDay1, cancellationToken, unitPrice: 1m);
        await client.RecordTransactionAsync(
            cashId, TransactionType.Withdraw, 4_000m, CashDay3, cancellationToken, unitPrice: 1m);

        var withdrawBetween = await client.PostCashTransactionAsync(cashId, TransactionType.Withdraw, 2_000m, CashDay2, cancellationToken);
        var editDeposit = await client.PutCashTransactionAsync(
            cashId, depositId, TransactionType.Deposit, 3_000m, CashDay1, cancellationToken);
        var deleteDeposit = await client.DeleteAsync(TransactionUri(cashId, depositId), cancellationToken);

        await withdrawBetween.AssertInsufficientFundsAsync(cancellationToken);
        await editDeposit.AssertInsufficientFundsAsync(cancellationToken);
        await deleteDeposit.AssertInsufficientFundsAsync(cancellationToken);
        Assert.Equal(1_000m, await client.GetCashBalanceAsync(accountId, cancellationToken));
        Assert.Equal(2, (await client.ListTransactionsAsync(cashId, cancellationToken)).Items.Count);
    }

    [Fact]
    public async Task SameDay_InflowFirst()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var depositFirst = await client.CreateAccountWithCashAsync(cancellationToken, name: "Deposit first");
        var withdrawFirst = await client.CreateAccountWithCashAsync(cancellationToken, balance: 500m, name: "Withdraw first");

        var depositThenWithdraw = new[]
        {
            await client.PostCashTransactionAsync(depositFirst.CashAssetId, TransactionType.Deposit, 500m, CashDay2, cancellationToken),
            await client.PostCashTransactionAsync(depositFirst.CashAssetId, TransactionType.Withdraw, 500m, CashDay2, cancellationToken)
        };
        var withdrawThenDeposit = new[]
        {
            await client.PostCashTransactionAsync(withdrawFirst.CashAssetId, TransactionType.Withdraw, 500m, CashDay2, cancellationToken),
            await client.PostCashTransactionAsync(withdrawFirst.CashAssetId, TransactionType.Deposit, 500m, CashDay2, cancellationToken)
        };

        Assert.All(depositThenWithdraw, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.All(withdrawThenDeposit, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(0m, await client.GetCashBalanceAsync(depositFirst.AccountId, cancellationToken));
        Assert.Equal(500m, await client.GetCashBalanceAsync(withdrawFirst.AccountId, cancellationToken));
    }

    [Fact]
    public async Task FreeCashCandidates_ExcludeAccountCash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateAccountWithCashAsync(cancellationToken, balance: 100m);
        var freeCashId = await client.AddCashAssetWithBalanceAsync(cancellationToken, name: "Wallet");

        var response = await client.GetAsync(TransferCandidatesUri("PLN", "Cash"), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var candidates = (await response.ReadJsonAsync(cancellationToken)).EnumerateArray().ToList();
        Assert.Equal(freeCashId, Assert.Single(candidates).GetProperty("assetId").GetGuid());
    }
}
