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

/// <summary>
/// deposit-payout-to-cash: <c>POST .../deposits/{assetId}/payout</c> with <c>{destinationAssetId, date}</c>
/// moves a settled deposit's whole balance to a Cash asset as a Deposit → Cash transfer on
/// <c>date</c> (<c>settledOn ≤ date ≤ today</c>, Europe/Warsaw), empties the deposit and marks it
/// PaidOut. The endpoint under test is called directly and asserted on its raw response; the
/// <see cref="PortfolioApi"/> helpers only arrange. The <c>AssetPositionChanged</c> per asset is proven
/// hostlessly by <see cref="PortfolioOutboxTests.PayOutDeposit_PublishesBothPositions"/>.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class PayOutDepositEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    /// <summary>
    /// AC-3: a deposit settled without a destination (10 119.83) is paid out later into a PLN Cash
    /// holding 5 000 — on <c>settledOn</c>, a day in between, or today, all three bounds inclusive.
    /// </summary>
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

    /// <summary>
    /// AC-4 (state half): an Active or Due deposit is not settled yet, a paid-out one has nothing left,
    /// and an archived portfolio is read-only — each a 409 with its own code, the destination and date
    /// valid, and nothing of the user's changes.
    /// </summary>
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

    /// <summary>
    /// AC-4 (input half): a date before <c>settledOn</c> or after today (Europe/Warsaw), or a
    /// destination outside the transfer rules — another currency, a non-Cash class, Cash in an
    /// archived portfolio, the deposit itself — is a 400, and nothing changes.
    /// </summary>
    [Theory]
    [InlineData("date-before-settled-on")]
    [InlineData("date-in-future")]
    [InlineData("eur-cash")]
    [InlineData("stock")]
    [InlineData("archived-portfolio-cash")]
    [InlineData("deposit-itself")]
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
}
