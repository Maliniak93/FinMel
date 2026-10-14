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
    public async Task ToReturnAccount(string payoutDate)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Today is Warsaw 2026-04-20; the deposit is settled on 2026-04-15.
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var mBank = await client.CreateBankAccountAsync(
            cancellationToken, "mBank", balance: 20_000m, depositedOn: DefaultTopUpDate);
        var nest = await client.CreateBankAccountAsync(cancellationToken, "Nest", SavingsVariant, balance: 500m, depositedOn: DefaultTopUpDate);
        var deposit = await client.AddDepositAsync(
            cancellationToken, NewDepositRequest(sourceAccountId: mBank.AccountId, returnAccountId: nest.AccountId));
        await client.SettleDepositAsync(deposit.AssetId, cancellationToken);
        var date = DateOnly.Parse(payoutDate, CultureInfo.InvariantCulture);

        var response = await client.PostAsJsonAsync(PayOutDepositUri(deposit.AssetId), NewPayOutRequest(date), cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Payout answered {(int)response.StatusCode}.");

        var withdraw = Assert.Single(
            (await client.ListTransactionsAsync(deposit.AssetId, cancellationToken)).Items,
            t => t.Type == TransactionType.Withdraw);
        Assert.Equal(10_119.83m, withdraw.Quantity);
        Assert.Equal(date, withdraw.Date);
        Assert.NotNull(withdraw.Transfer);
        Assert.Equal(nest.MoneyAssetId, withdraw.Transfer.CounterpartAssetId);
        Assert.Equal(TransferDirection.Out, withdraw.Transfer.Direction);

        var nestLeg = Assert.Single(
            (await client.ListTransactionsAsync(nest.MoneyAssetId, cancellationToken)).Items, t => t.Transfer is not null);
        Assert.Equal(TransactionType.Deposit, nestLeg.Type);
        Assert.Equal(10_119.83m, nestLeg.Quantity);
        Assert.Equal(date, nestLeg.Date);
        Assert.Equal(deposit.AssetId, nestLeg.Transfer!.CounterpartAssetId);
        Assert.Equal(TransferDirection.In, nestLeg.Transfer.Direction);

        await using (var dbContext = CreateDbContext(userId))
        {
            var payoutLegs = await dbContext.Transactions
                .Where(t => t.TransferId != null && t.Type == TransactionType.Withdraw && t.AssetId == deposit.AssetId)
                .ToListAsync(cancellationToken);
            var payoutTransferId = Assert.Single(payoutLegs).TransferId;
            Assert.Equal(2, await dbContext.Transactions.CountAsync(t => t.TransferId == payoutTransferId, cancellationToken));
        }

        Assert.Equal(0m, (await client.GetAssetAsync(deposit.AssetId, cancellationToken)).Quantity);
        Assert.Equal(10_619.83m, await client.GetCashBalanceAsync(nest.AccountId, cancellationToken));
        Assert.Equal(10_000m, await client.GetCashBalanceAsync(mBank.AccountId, cancellationToken));
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(deposit.AssetId, cancellationToken);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(nest.MoneyAssetId, cancellationToken);

        var paidOut = await client.GetDepositAsync(deposit.AssetId, cancellationToken);
        Assert.Equal(DepositStatus.PaidOut, paidOut.Status);
        Assert.Equal(new DateOnly(2026, 4, 15), paidOut.SettledOn);
        Assert.Equal(date, paidOut.PaidOutOn);
        Assert.Equal("Nest", paidOut.PaidOutToAccountName);
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

        var response = await client.PostAsJsonAsync(PayOutDepositUri(depositId), NewPayOutRequest(), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, errorCode, cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
        Assert.Equal(statusBefore, (await client.GetDepositAsync(depositId, cancellationToken)).Status);
    }

    [Fact]
    public async Task PayOut_ArchivedDeposit_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var deposit = await client.AddDepositAsync(cancellationToken);
        await client.SettleDepositAsync(deposit.AssetId, cancellationToken);
        await client.ArchiveAssetAsync(deposit.AssetId, cancellationToken);
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.PostAsJsonAsync(PayOutDepositUri(deposit.AssetId), NewPayOutRequest(), cancellationToken);

        await response.AssertAssetArchivedConflictAsync(cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
        Assert.Equal(10_119.83m, (await client.GetAssetAsync(deposit.AssetId, cancellationToken)).Quantity);
    }

    [Theory]
    [InlineData("date-before-settled-on")]
    [InlineData("date-in-future")]
    public async Task PayOut_InvalidDate_ReturnsBadRequest(string invalidCase)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Today is Warsaw 2026-04-20; the deposit is settled on 2026-04-15.
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var deposit = await client.AddDepositAsync(cancellationToken);
        await client.SettleDepositAsync(deposit.AssetId, cancellationToken);
        var request = invalidCase switch
        {
            "date-before-settled-on" => NewPayOutRequest(new DateOnly(2026, 4, 14)),
            "date-in-future" => NewPayOutRequest(new DateOnly(2026, 4, 21)),
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase), invalidCase, null)
        };
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.PostAsJsonAsync(PayOutDepositUri(deposit.AssetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
        var unchanged = await client.GetDepositAsync(deposit.AssetId, cancellationToken);
        Assert.Equal(DepositStatus.Settled, unchanged.Status);
        Assert.Null(unchanged.PaidOutOn);
        Assert.Null(unchanged.PaidOutToAccountName);
        Assert.Equal(10_119.83m, (await client.GetAssetAsync(deposit.AssetId, cancellationToken)).Quantity);
    }
}
