using System.Net;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Bonds;
using Skarbiec.Portfolio.Features.Transfers;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

// The early redemption under test is called directly; fixture helpers only arrange.
[Collection(TestingDefaults.CollectionName)]
public sealed class RedeemBondEarlyEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Early_PartialCapitalising_ShrinksHolding()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(EdoEarlyRedemptionDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateEdoYearOneSettledAsync(cancellationToken);
        var portfolioId = funded.BondPortfolioId;
        var assetId = funded.Bond.AssetId;

        var response = await client.RedeemBondEarlyRawAsync(
            portfolioId, assetId, EdoEarlyRedemptionDate, 4, funded.CashAssetId, cancellationToken, runningPeriodRatePercent: 4.00m);

        Assert.True(response.IsSuccessStatusCode, $"Early redemption answered {(int)response.StatusCode}.");

        var row = Assert.Single(await ReadBondRedemptionsAsync(userId, cancellationToken, assetId));
        Assert.Equal("Early", row.Kind.ToString());
        Assert.Equal(EdoEarlyRedemptionDate, row.Date);
        Assert.Equal(4, row.BondCount);
        Assert.Equal(24.76m, row.AccruedInterest);
        Assert.Equal(12.00m, row.Fee);
        Assert.Equal(0m, row.DiscountIncome);
        Assert.Equal(2.43m, row.Tax);
        Assert.Equal(410.33m, row.Proceeds);
        Assert.NotNull(row.CreditTransactionId);
        Assert.NotNull(row.ChargeTransactionId);
        Assert.NotNull(row.CashTransferId);

        var bondTransactions = (await client.ListTransactionsAsync(portfolioId, assetId, cancellationToken)).Items;
        var credit = Assert.Single(
            bondTransactions,
            t => t is { Type: TransactionType.Deposit, Transfer: null, BondInterestPeriodIndex: null } && t.Date == EdoEarlyRedemptionDate);
        Assert.Equal(3.36m, credit.Quantity);
        var charge = Assert.Single(
            bondTransactions, t => t is { Type: TransactionType.Withdraw, Transfer: null } && t.Date == EdoEarlyRedemptionDate);
        Assert.Equal(14.43m, charge.Quantity);
        var payout = Assert.Single(bondTransactions, t => t.Transfer?.Direction == TransferDirection.Out);
        Assert.Equal(410.33m, payout.Quantity);
        Assert.Equal(EdoEarlyRedemptionDate, payout.Date);
        Assert.Equal(funded.CashAssetId, payout.Transfer!.CounterpartAssetId);

        Assert.Equal(632.10m, (await client.GetAssetAsync(portfolioId, assetId, cancellationToken)).Quantity);
        Assert.Equal(5_410.33m, (await client.GetAssetAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken)).Quantity);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(portfolioId, assetId, cancellationToken);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken);

        var bond = await client.GetBondAsync(portfolioId, assetId, cancellationToken);
        Assert.Equal(6, bond.BondCount);
        Assert.Equal(632.10m, bond.BookValue);
        Assert.Equal(BondStatus.Active, bond.Status);
    }

    [Fact]
    public async Task Early_CouponAfterPeriod1_ChargesFullFee()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(RorEarlyRedemptionDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateRorWithThreeMonthsSettledAsync(cancellationToken);
        var portfolioId = funded.BondPortfolioId;
        var assetId = funded.Bond.AssetId;
        var cashBefore = (await client.GetAssetAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken)).Quantity;

        var response = await client.RedeemBondEarlyRawAsync(
            portfolioId, assetId, RorEarlyRedemptionDate, 20, funded.CashAssetId, cancellationToken, runningPeriodRatePercent: 3.75m);

        Assert.True(response.IsSuccessStatusCode, $"Early redemption answered {(int)response.StatusCode}.");

        var row = Assert.Single(await ReadBondRedemptionsAsync(userId, cancellationToken, assetId));
        Assert.Equal("Early", row.Kind.ToString());
        Assert.Equal(20, row.BondCount);
        Assert.Equal(3.20m, row.AccruedInterest);
        Assert.Equal(10.00m, row.Fee);
        Assert.Equal(0m, row.Tax);
        Assert.Equal(1_993.20m, row.Proceeds);
        Assert.NotNull(row.CreditTransactionId);
        Assert.NotNull(row.ChargeTransactionId);

        var bondTransactions = (await client.ListTransactionsAsync(portfolioId, assetId, cancellationToken)).Items;
        var credit = Assert.Single(
            bondTransactions,
            t => t is { Type: TransactionType.Deposit, Transfer: null, BondInterestPeriodIndex: null } && t.Date == RorEarlyRedemptionDate);
        Assert.Equal(3.20m, credit.Quantity);
        var charge = Assert.Single(
            bondTransactions, t => t is { Type: TransactionType.Withdraw, Transfer: null } && t.Date == RorEarlyRedemptionDate);
        Assert.Equal(10.00m, charge.Quantity);

        Assert.Equal(0m, (await client.GetAssetAsync(portfolioId, assetId, cancellationToken)).Quantity);
        Assert.Equal(
            cashBefore + 1_993.20m,
            (await client.GetAssetAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken)).Quantity);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(portfolioId, assetId, cancellationToken);

        var bond = await client.GetBondAsync(portfolioId, assetId, cancellationToken);
        Assert.Equal(BondStatus.Redeemed, bond.Status);
        Assert.Equal(0m, bond.BookValue);
    }

    public static TheoryData<string, HttpStatusCode, string> InvalidRequests() => new()
    {
        { "date-on-purchase-date", HttpStatusCode.BadRequest, PortfolioAssertions.BondEarlyRedemptionDateErrorCode },
        { "date-on-maturity", HttpStatusCode.BadRequest, PortfolioAssertions.BondEarlyRedemptionDateErrorCode },
        { "date-in-future", HttpStatusCode.BadRequest, PortfolioAssertions.BondEarlyRedemptionDateErrorCode },
        { "count-zero", HttpStatusCode.BadRequest, PortfolioAssertions.BondRedemptionCountErrorCode },
        { "count-above-holding", HttpStatusCode.BadRequest, PortfolioAssertions.BondRedemptionCountErrorCode },
        { "variable-rate-missing", HttpStatusCode.BadRequest, PortfolioAssertions.BondPeriodRateErrorCode },
        { "unsettled-earlier-period", HttpStatusCode.Conflict, PortfolioAssertions.BondInterestUnsettledErrorCode },
        { "date-before-settled-period-end", HttpStatusCode.Conflict, PortfolioAssertions.BondEarlyRedemptionOutOfOrderErrorCode },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Early_Invalid_IsRejected(string scenario, HttpStatusCode status, string errorCode)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(EdoEarlyRedemptionDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateEdoYearOneSettledAsync(cancellationToken, settleYearOne: scenario != "unsettled-earlier-period");
        var portfolioId = funded.BondPortfolioId;
        var assetId = funded.Bond.AssetId;
        var rowsBefore = await SnapshotUserRowsAsync(userId, cancellationToken);

        var date = EdoEarlyRedemptionDate;
        var count = 4;
        decimal? rate = 4.00m;
        switch (scenario)
        {
            case "date-on-purchase-date":
                date = EdoEarlyPurchaseDate;
                rate = null;
                break;
            case "date-on-maturity":
                date = funded.Bond.MaturityDate;
                break;
            case "date-in-future":
                date = EdoEarlyRedemptionDate.AddDays(1);
                break;
            case "count-zero":
                count = 0;
                break;
            case "count-above-holding":
                count = 11;
                break;
            case "variable-rate-missing":
                rate = null;
                break;
            case "date-before-settled-period-end":
                date = new DateOnly(2026, 2, 15);
                rate = null;
                break;
        }

        var response = await client.RedeemBondEarlyRawAsync(
            portfolioId, assetId, date, count, funded.CashAssetId, cancellationToken, rate);

        await response.AssertProblemAsync(status, errorCode, cancellationToken);
        Assert.Equal(rowsBefore, await SnapshotUserRowsAsync(userId, cancellationToken));
        Assert.Empty(await ReadBondRedemptionsAsync(userId, cancellationToken, assetId));
    }
}
