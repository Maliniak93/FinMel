using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.Transfers;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class PayOutDepositEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Theory]
    [InlineData("2026-04-15")]
    [InlineData("2026-04-18")]
    [InlineData("2026-04-20")]
    public async Task PayOut_SettledDeposit_MovesWholeBalance(string payoutDate)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Today is Warsaw 2026-04-20; the deposit is settled on 2026-04-15.
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var deposit = await client.AddDepositAsync(cancellationToken);
        await client.SettleDepositAsync(deposit.AssetId, cancellationToken);
        var cashId = await client.AddCashAssetWithBalanceAsync(cancellationToken, balance: 5_000m);
        var date = DateOnly.Parse(payoutDate, CultureInfo.InvariantCulture);

        var response = await client.PostAsJsonAsync(
            PayOutDepositUri(deposit.AssetId), NewPayOutRequest(cashId, date), cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Payout answered {(int)response.StatusCode}.");

        var withdraw = Assert.Single(
            (await client.ListTransactionsAsync(deposit.AssetId, cancellationToken)).Items,
            t => t.Type == TransactionType.Withdraw);
        Assert.Equal(10_119.83m, withdraw.Quantity);
        Assert.Equal(date, withdraw.Date);
        Assert.NotNull(withdraw.Transfer);
        Assert.Equal(cashId, withdraw.Transfer.CounterpartAssetId);
        Assert.Equal(TransferDirection.Out, withdraw.Transfer.Direction);

        var cashTransactions = (await client.ListTransactionsAsync(cashId, cancellationToken)).Items;
        Assert.Equal(2, cashTransactions.Count);
        var cashLeg = Assert.Single(cashTransactions, t => t.Transfer is not null);
        Assert.Equal(TransactionType.Deposit, cashLeg.Type);
        Assert.Equal(10_119.83m, cashLeg.Quantity);
        Assert.Equal(date, cashLeg.Date);
        Assert.Equal(deposit.AssetId, cashLeg.Transfer!.CounterpartAssetId);
        Assert.Equal(TransferDirection.In, cashLeg.Transfer.Direction);

        await using (var dbContext = CreateDbContext(userId))
        {
            var legs = await dbContext.Transactions.Where(t => t.TransferId != null).ToListAsync(cancellationToken);
            Assert.Equal(2, legs.Count);
            Assert.Single(legs.Select(t => t.TransferId).Distinct());
        }

        Assert.Equal(0m, (await client.GetAssetAsync(deposit.AssetId, cancellationToken)).Quantity);
        Assert.Equal(15_119.83m, (await client.GetAssetAsync(cashId, cancellationToken)).Quantity);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(deposit.AssetId, cancellationToken);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(cashId, cancellationToken);

        var paidOut = await client.GetDepositAsync(deposit.AssetId, cancellationToken);
        Assert.Equal(DepositStatus.PaidOut, paidOut.Status);
        Assert.Equal(new DateOnly(2026, 4, 15), paidOut.SettledOn);
        Assert.Equal(date, paidOut.PaidOutOn);
        Assert.Equal("Cash account", paidOut.PaidOutToAssetName);
    }

    [Theory]
    [InlineData("active", PortfolioAssertions.DepositNotSettledErrorCode)]
    [InlineData("due", PortfolioAssertions.DepositNotSettledErrorCode)]
    [InlineData("paid-out", PortfolioAssertions.DepositAlreadyPaidOutErrorCode)]
    public async Task PayOut_InvalidState_ReturnsConflict(string invalidCase, string errorCode)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var cashId = await client.AddCashAssetWithBalanceAsync(cancellationToken, name: "Current account");
        Guid depositId;
        switch (invalidCase)
        {
            case "active":
                // Starts 2026-04-01 for 3 months — matures 2026-07-01, after today.
                var active = await client.AddDepositAsync(
                    cancellationToken, NewDepositRequest(startDate: new DateOnly(2026, 4, 1)));
                depositId = active.AssetId;
                break;
            case "due":
                var due = await client.AddDepositAsync(cancellationToken);
                depositId = due.AssetId;
                break;
            case "paid-out":
                var paidOut = await client.CreatePaidOutDepositAsync(cancellationToken);
                depositId = paidOut.Deposit.AssetId;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(invalidCase), invalidCase, null);
        }

        var statusBefore = (await client.GetDepositAsync(depositId, cancellationToken)).Status;
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.PostAsJsonAsync(
            PayOutDepositUri(depositId), NewPayOutRequest(cashId), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, errorCode, cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
        Assert.Equal(statusBefore, (await client.GetDepositAsync(depositId, cancellationToken)).Status);
        await client.AssertCashUntouchedAsync(cashId, cancellationToken);
    }

    [Fact]
    public async Task PayOut_ArchivedDeposit_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var cashId = await client.AddCashAssetWithBalanceAsync(cancellationToken, name: "Current account");
        var deposit = await client.AddDepositAsync(cancellationToken);
        await client.SettleDepositAsync(deposit.AssetId, cancellationToken);
        await client.ArchiveAssetAsync(deposit.AssetId, cancellationToken);
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.PostAsJsonAsync(
            PayOutDepositUri(deposit.AssetId), NewPayOutRequest(cashId), cancellationToken);

        await response.AssertAssetArchivedConflictAsync(cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
        await client.AssertCashUntouchedAsync(cashId, cancellationToken);
        Assert.Equal(10_119.83m, (await client.GetAssetAsync(deposit.AssetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task PayOut_ToArchivedCash_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var deposit = await client.AddDepositAsync(cancellationToken);
        await client.SettleDepositAsync(deposit.AssetId, cancellationToken);
        var cashId = await client.AddArchivedCashAssetAsync(cancellationToken);
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.PostAsJsonAsync(
            PayOutDepositUri(deposit.AssetId), NewPayOutRequest(cashId), cancellationToken);

        await response.AssertInvalidTransferCounterpartAsync(cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
        await client.AssertCashUntouchedAsync(cashId, cancellationToken);
        Assert.Equal(10_119.83m, (await client.GetAssetAsync(deposit.AssetId, cancellationToken)).Quantity);
    }

    [Theory]
    [InlineData("date-before-settled-on")]
    [InlineData("date-in-future")]
    [InlineData("stock")]
    [InlineData("archived-cash")]
    [InlineData("deposit-itself")]
    [InlineData("archived-bank-account")]
    public async Task PayOut_InvalidInput_ReturnsBadRequest(string invalidCase)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Today is Warsaw 2026-04-20; the deposit is settled on 2026-04-15.
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var deposit = await client.AddDepositAsync(cancellationToken);
        await client.SettleDepositAsync(deposit.AssetId, cancellationToken);
        var cashId = await client.AddCashAssetAsync(cancellationToken);
        var request = invalidCase switch
        {
            "date-before-settled-on" => NewPayOutRequest(cashId, new DateOnly(2026, 4, 14)),
            "date-in-future" => NewPayOutRequest(cashId, new DateOnly(2026, 4, 21)),
            "stock" => NewPayOutRequest(
                await client.AddAssetAsync(await client.CreateAccountAsync(cancellationToken), cancellationToken, name: "Some stock")),
            "archived-cash" => NewPayOutRequest(await client.AddArchivedCashAssetAsync(cancellationToken)),
            "deposit-itself" => NewPayOutRequest(deposit.AssetId),
            "archived-bank-account" => NewPayOutRequest(await client.CreateArchivedBankAccountAsync(cancellationToken)),
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase), invalidCase, null)
        };
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.PostAsJsonAsync(PayOutDepositUri(deposit.AssetId), request, cancellationToken);

        if (invalidCase.StartsWith("date-", StringComparison.Ordinal))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        else
        {
            await response.AssertInvalidTransferCounterpartAsync(cancellationToken);
        }

        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
        var unchanged = await client.GetDepositAsync(deposit.AssetId, cancellationToken);
        Assert.Equal(DepositStatus.Settled, unchanged.Status);
        Assert.Null(unchanged.PaidOutOn);
        Assert.Null(unchanged.PaidOutToAssetName);
        Assert.Equal(10_119.83m, (await client.GetAssetAsync(deposit.AssetId, cancellationToken)).Quantity);
        Assert.Equal(0m, (await client.GetAssetAsync(cashId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task ToForeignCurrencyCash_ReturnsTransferCurrencyMismatch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var deposit = await client.AddDepositAsync(cancellationToken);
        await client.SettleDepositAsync(deposit.AssetId, cancellationToken);
        var eurCash = await client.AddCashAssetAsync(cancellationToken, currency: "EUR", name: "EUR account");

        var response = await client.PostAsJsonAsync(
            PayOutDepositUri(deposit.AssetId), NewPayOutRequest(eurCash), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, TransferCurrencyMismatchErrorCode, cancellationToken);
        Assert.Equal(10_119.83m, (await client.GetAssetAsync(deposit.AssetId, cancellationToken)).Quantity);
        Assert.Equal(0m, (await client.GetAssetAsync(eurCash, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task ToBankAccount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var deposit = await client.AddDepositAsync(cancellationToken);
        await client.SettleDepositAsync(deposit.AssetId, cancellationToken);
        var mBank = await client.CreateBankAccountAsync(
            cancellationToken, balance: 500m, depositedOn: new DateOnly(2026, 4, 1));
        var date = new DateOnly(2026, 4, 18);

        var response = await client.PostAsJsonAsync(
            PayOutDepositUri(deposit.AssetId), NewPayOutRequest(mBank.MoneyAssetId, date), cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Payout answered {(int)response.StatusCode}.");
        Assert.Equal(10_619.83m, await client.GetCashBalanceAsync(mBank.AccountId, cancellationToken));
        Assert.Equal(0m, (await client.GetAssetAsync(deposit.AssetId, cancellationToken)).Quantity);
        var bankLeg = Assert.Single(
            (await client.ListTransactionsAsync(mBank.MoneyAssetId, cancellationToken)).Items, t => t.Transfer is not null);
        Assert.Equal(TransactionType.Deposit, bankLeg.Type);
        Assert.Equal(10_119.83m, bankLeg.Quantity);
        Assert.Equal(date, bankLeg.Date);
        Assert.Equal(deposit.AssetId, bankLeg.Transfer!.CounterpartAssetId);
        var withdraw = Assert.Single(
            (await client.ListTransactionsAsync(deposit.AssetId, cancellationToken)).Items,
            t => t.Type == TransactionType.Withdraw);
        Assert.Equal(10_119.83m, withdraw.Quantity);
        Assert.Equal(mBank.MoneyAssetId, withdraw.Transfer!.CounterpartAssetId);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(deposit.AssetId, cancellationToken);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(mBank.MoneyAssetId, cancellationToken);
        var paidOut = await client.GetDepositAsync(deposit.AssetId, cancellationToken);
        Assert.Equal(DepositStatus.PaidOut, paidOut.Status);
        Assert.Equal(date, paidOut.PaidOutOn);
        Assert.Equal("mBank", paidOut.PaidOutToAssetName);
    }

    [Fact]
    public async Task DifferentCurrency_Returns400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var deposit = await client.AddDepositAsync(cancellationToken);
        await client.SettleDepositAsync(deposit.AssetId, cancellationToken);
        var usd = await client.CreateBankAccountAsync(cancellationToken, "mBank USD", currency: "USD");
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.PostAsJsonAsync(
            PayOutDepositUri(deposit.AssetId), NewPayOutRequest(usd.MoneyAssetId), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, TransferCurrencyMismatchErrorCode, cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
        Assert.Equal(10_119.83m, (await client.GetAssetAsync(deposit.AssetId, cancellationToken)).Quantity);
    }
}
