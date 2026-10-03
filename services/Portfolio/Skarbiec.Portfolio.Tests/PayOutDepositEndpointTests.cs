using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.Transfers;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
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
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        await client.SettleDepositAsync(portfolioId, deposit.AssetId, cancellationToken);
        var walletId = await client.CreatePortfolioAsync(cancellationToken, name: "Wallet");
        var cashId = await client.AddCashAssetWithBalanceAsync(walletId, cancellationToken, balance: 5_000m);
        var date = DateOnly.Parse(payoutDate, CultureInfo.InvariantCulture);

        var response = await client.PostAsJsonAsync(
            PayOutDepositUri(portfolioId, deposit.AssetId), NewPayOutRequest(cashId, date), cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Payout answered {(int)response.StatusCode}.");

        var withdraw = Assert.Single(
            (await client.ListTransactionsAsync(portfolioId, deposit.AssetId, cancellationToken)).Items,
            t => t.Type == TransactionType.Withdraw);
        Assert.Equal(10_119.83m, withdraw.Quantity);
        Assert.Equal(date, withdraw.Date);
        Assert.NotNull(withdraw.Transfer);
        Assert.Equal(cashId, withdraw.Transfer.CounterpartAssetId);
        Assert.Equal(TransferDirection.Out, withdraw.Transfer.Direction);

        var cashTransactions = (await client.ListTransactionsAsync(walletId, cashId, cancellationToken)).Items;
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

        Assert.Equal(0m, (await client.GetAssetAsync(portfolioId, deposit.AssetId, cancellationToken)).Quantity);
        Assert.Equal(15_119.83m, (await client.GetAssetAsync(walletId, cashId, cancellationToken)).Quantity);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(portfolioId, deposit.AssetId, cancellationToken);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(walletId, cashId, cancellationToken);

        var paidOut = await client.GetDepositAsync(portfolioId, deposit.AssetId, cancellationToken);
        Assert.Equal(DepositStatus.PaidOut, paidOut.Status);
        Assert.Equal(new DateOnly(2026, 4, 15), paidOut.SettledOn);
        Assert.Equal(date, paidOut.PaidOutOn);
        Assert.Equal("Cash account", paidOut.PaidOutToAssetName);
    }

    [Theory]
    [InlineData("active", PortfolioAssertions.DepositNotSettledErrorCode)]
    [InlineData("due", PortfolioAssertions.DepositNotSettledErrorCode)]
    [InlineData("paid-out", PortfolioAssertions.DepositAlreadyPaidOutErrorCode)]
    [InlineData("archived-portfolio", PortfolioAssertions.PortfolioArchivedErrorCode)]
    public async Task PayOut_InvalidState_ReturnsConflict(string invalidCase, string errorCode)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var walletId = await client.CreatePortfolioAsync(cancellationToken, name: "Current accounts");
        var cashId = await client.AddCashAssetWithBalanceAsync(walletId, cancellationToken, name: "Current account");
        Guid portfolioId;
        Guid depositId;
        switch (invalidCase)
        {
            case "active":
                // Starts 2026-04-01 for 3 months — matures 2026-07-01, after today.
                (portfolioId, var active) = await client.CreatePortfolioWithDepositAsync(
                    cancellationToken, NewDepositRequest(startDate: new DateOnly(2026, 4, 1)));
                depositId = active.AssetId;
                break;
            case "due":
                (portfolioId, var due) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
                depositId = due.AssetId;
                break;
            case "paid-out":
                var paidOut = await client.CreatePaidOutDepositAsync(cancellationToken);
                (portfolioId, depositId) = (paidOut.DepositPortfolioId, paidOut.Deposit.AssetId);
                break;
            case "archived-portfolio":
                (portfolioId, var settled) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
                depositId = settled.AssetId;
                await client.SettleDepositAsync(portfolioId, depositId, cancellationToken);
                await client.ArchivePortfolioAsync(portfolioId, cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(invalidCase), invalidCase, null);
        }

        var statusBefore = (await client.GetDepositAsync(portfolioId, depositId, cancellationToken)).Status;
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.PostAsJsonAsync(
            PayOutDepositUri(portfolioId, depositId), NewPayOutRequest(cashId), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, errorCode, cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
        Assert.Equal(statusBefore, (await client.GetDepositAsync(portfolioId, depositId, cancellationToken)).Status);
        await client.AssertCashUntouchedAsync(walletId, cashId, cancellationToken);
    }

    [Fact]
    public async Task PayOut_ArchivedDeposit_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var walletId = await client.CreatePortfolioAsync(cancellationToken, name: "Current accounts");
        var cashId = await client.AddCashAssetWithBalanceAsync(walletId, cancellationToken, name: "Current account");
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        await client.SettleDepositAsync(portfolioId, deposit.AssetId, cancellationToken);
        await client.ArchiveAssetAsync(portfolioId, deposit.AssetId, cancellationToken);
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.PostAsJsonAsync(
            PayOutDepositUri(portfolioId, deposit.AssetId), NewPayOutRequest(cashId), cancellationToken);

        await response.AssertAssetArchivedConflictAsync(cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
        await client.AssertCashUntouchedAsync(walletId, cashId, cancellationToken);
        Assert.Equal(10_119.83m, (await client.GetAssetAsync(portfolioId, deposit.AssetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task PayOut_ToArchivedCash_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        await client.SettleDepositAsync(portfolioId, deposit.AssetId, cancellationToken);
        var (cashPortfolioId, cashId) = await client.AddArchivedCashAssetInLivePortfolioAsync(cancellationToken);
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.PostAsJsonAsync(
            PayOutDepositUri(portfolioId, deposit.AssetId), NewPayOutRequest(cashId), cancellationToken);

        await response.AssertInvalidTransferCounterpartAsync(cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
        await client.AssertCashUntouchedAsync(cashPortfolioId, cashId, cancellationToken);
        Assert.Equal(10_119.83m, (await client.GetAssetAsync(portfolioId, deposit.AssetId, cancellationToken)).Quantity);
    }

    [Theory]
    [InlineData("date-before-settled-on")]
    [InlineData("date-in-future")]
    [InlineData("eur-cash")]
    [InlineData("stock")]
    [InlineData("archived-portfolio-cash")]
    [InlineData("deposit-itself")]
    [InlineData("eur-savings")]
    [InlineData("archived-portfolio-savings")]
    public async Task PayOut_InvalidInput_ReturnsBadRequest(string invalidCase)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Today is Warsaw 2026-04-20; the deposit is settled on 2026-04-15.
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        await client.SettleDepositAsync(portfolioId, deposit.AssetId, cancellationToken);
        var walletId = await client.CreatePortfolioAsync(cancellationToken, name: "Wallet");
        var cashId = await client.AddCashAssetAsync(walletId, cancellationToken);
        var request = invalidCase switch
        {
            "date-before-settled-on" => NewPayOutRequest(cashId, new DateOnly(2026, 4, 14)),
            "date-in-future" => NewPayOutRequest(cashId, new DateOnly(2026, 4, 21)),
            "eur-cash" => NewPayOutRequest(
                await client.AddCashAssetAsync(walletId, cancellationToken, currency: "EUR", name: "EUR account")),
            "stock" => NewPayOutRequest(await client.AddAssetAsync(walletId, cancellationToken, name: "Some stock")),
            "archived-portfolio-cash" => NewPayOutRequest((await client.AddArchivedCashAssetAsync(cancellationToken)).CashId),
            "deposit-itself" => NewPayOutRequest(deposit.AssetId),
            "eur-savings" => NewPayOutRequest(
                (await client.AddSavingsAccountAsync(
                    walletId, cancellationToken, NewSavingsAccountRequest(currency: "EUR", withOpeningDeposit: false))).AssetId),
            "archived-portfolio-savings" => NewPayOutRequest((await client.AddArchivedSavingsAccountAsync(cancellationToken)).SavingsId),
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase), invalidCase, null)
        };
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.PostAsJsonAsync(PayOutDepositUri(portfolioId, deposit.AssetId), request, cancellationToken);

        if (invalidCase.StartsWith("date-", StringComparison.Ordinal))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        else
        {
            await response.AssertInvalidTransferCounterpartAsync(cancellationToken);
        }

        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
        var unchanged = await client.GetDepositAsync(portfolioId, deposit.AssetId, cancellationToken);
        Assert.Equal(DepositStatus.Settled, unchanged.Status);
        Assert.Null(unchanged.PaidOutOn);
        Assert.Null(unchanged.PaidOutToAssetName);
        Assert.Equal(10_119.83m, (await client.GetAssetAsync(portfolioId, deposit.AssetId, cancellationToken)).Quantity);
        Assert.Equal(0m, (await client.GetAssetAsync(walletId, cashId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task PayOut_ToSavingsAccount_MovesWholeBalance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        await client.SettleDepositAsync(portfolioId, deposit.AssetId, cancellationToken);
        var (savingsPortfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(
            cancellationToken,
            NewSavingsAccountRequest(openingAmount: 500m, openingDate: new DateOnly(2026, 4, 1)),
            portfolioName: "Wallet");
        var date = new DateOnly(2026, 4, 18);

        var response = await client.PostAsJsonAsync(
            PayOutDepositUri(portfolioId, deposit.AssetId), NewPayOutRequest(account.AssetId, date), cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Payout answered {(int)response.StatusCode}.");

        var withdraw = Assert.Single(
            (await client.ListTransactionsAsync(portfolioId, deposit.AssetId, cancellationToken)).Items,
            t => t.Type == TransactionType.Withdraw);
        Assert.Equal(10_119.83m, withdraw.Quantity);
        Assert.Equal(date, withdraw.Date);
        Assert.Equal(account.AssetId, withdraw.Transfer!.CounterpartAssetId);

        var savingsLeg = Assert.Single(
            (await client.ListTransactionsAsync(savingsPortfolioId, account.AssetId, cancellationToken)).Items,
            t => t.Transfer is not null);
        Assert.Equal(TransactionType.Deposit, savingsLeg.Type);
        Assert.Equal(10_119.83m, savingsLeg.Quantity);
        Assert.Equal(date, savingsLeg.Date);
        Assert.Equal(deposit.AssetId, savingsLeg.Transfer!.CounterpartAssetId);

        Assert.Equal(0m, (await client.GetAssetAsync(portfolioId, deposit.AssetId, cancellationToken)).Quantity);
        Assert.Equal(10_619.83m, (await client.GetAssetAsync(savingsPortfolioId, account.AssetId, cancellationToken)).Quantity);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(portfolioId, deposit.AssetId, cancellationToken);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(savingsPortfolioId, account.AssetId, cancellationToken);

        var paidOut = await client.GetDepositAsync(portfolioId, deposit.AssetId, cancellationToken);
        Assert.Equal(DepositStatus.PaidOut, paidOut.Status);
        Assert.Equal(date, paidOut.PaidOutOn);
        Assert.Equal(account.Name, paidOut.PaidOutToAssetName);
    }
}
