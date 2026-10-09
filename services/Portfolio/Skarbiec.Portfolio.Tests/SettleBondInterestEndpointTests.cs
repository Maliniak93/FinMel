using System.Net;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class SettleBondInterestEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    private static readonly DateTimeOffset EdoYearOneEndedUtc = new(2027, 10, 2, 10, 0, 0, TimeSpan.Zero);

    private static readonly (int PeriodIndex, decimal? RatePercent)[] RorThreeMonths = [(1, null), (2, 3.75m), (3, 3.75m)];

    [Fact]
    public async Task Settle_Capitalising_CreditsGross()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(EdoYearOneEndedUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, bond) = await client.CreatePortfolioWithBondAsync(cancellationToken);

        var response = await client.SettleBondInterestRawAsync(portfolioId, bond.AssetId, [(1, null)], null, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using (var dbContext = CreateDbContext(userId))
        {
            var settlement = await dbContext.Set<BondInterestSettlement>().SingleAsync(s => s.AssetId == bond.AssetId, cancellationToken);
            Assert.Equal(1, settlement.PeriodIndex);
            Assert.Equal(new DateOnly(2026, 10, 1), settlement.PeriodStart);
            Assert.Equal(new DateOnly(2027, 10, 1), settlement.PeriodEnd);
            Assert.Equal(5.35m, settlement.RatePercent);
            Assert.Equal(50, settlement.BondCount);
            Assert.Equal(267.50m, settlement.GrossInterest);
            Assert.Equal(0m, settlement.Tax);
            Assert.NotNull(settlement.CreditTransactionId);
            Assert.Null(settlement.TransferId);
        }

        var transactions = (await client.ListTransactionsAsync(portfolioId, bond.AssetId, cancellationToken)).Items;
        Assert.Equal(2, transactions.Count);
        var credit = Assert.Single(transactions, t => t.BondInterestPeriodIndex is not null);
        Assert.Equal(1, credit.BondInterestPeriodIndex);
        Assert.Equal(TransactionType.Deposit, credit.Type);
        Assert.Equal(267.50m, credit.Quantity);
        Assert.Equal(new DateOnly(2027, 10, 1), credit.Date);
        Assert.Equal(5_267.50m, (await client.GetAssetAsync(portfolioId, bond.AssetId, cancellationToken)).Quantity);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(portfolioId, bond.AssetId, cancellationToken);
    }

    [Fact]
    public async Task Settle_AfterPartialRedemption_UsesRemainingCount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(EdoEarlyRedemptionDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateEdoYearOneSettledAsync(cancellationToken);
        var portfolioId = funded.BondPortfolioId;
        var assetId = funded.Bond.AssetId;
        await client.RedeemBondEarlyAsync(
            portfolioId, assetId, EdoEarlyRedemptionDate, 4, funded.CashAssetId, cancellationToken, runningPeriodRatePercent: 4.00m);
        Factory.Clock.SetUtcNow(new DateTimeOffset(2027, 3, 2, 10, 0, 0, TimeSpan.Zero));

        var response = await client.SettleBondInterestRawAsync(portfolioId, assetId, [(2, 4.00m)], null, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using (var dbContext = CreateDbContext(userId))
        {
            var settlement = await dbContext.Set<BondInterestSettlement>()
                .SingleAsync(s => s.AssetId == assetId && s.PeriodIndex == 2, cancellationToken);
            Assert.Equal(6, settlement.BondCount);
            Assert.Equal(25.26m, settlement.GrossInterest);
        }

        Assert.Equal(657.36m, (await client.GetAssetAsync(portfolioId, assetId, cancellationToken)).Quantity);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(portfolioId, assetId, cancellationToken);
    }

    [Fact]
    public async Task Settle_Coupon_PaysNetToCash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateFundedBondAsync(cancellationToken, request: NewRorBondRequest());

        var response = await client.SettleBondInterestRawAsync(
            funded.BondPortfolioId, funded.Bond.AssetId, RorThreeMonths, funded.CashAssetId, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using (var dbContext = CreateDbContext(userId))
        {
            var settlements = await dbContext.Set<BondInterestSettlement>()
                .Where(s => s.AssetId == funded.Bond.AssetId)
                .OrderBy(s => s.PeriodIndex)
                .ToListAsync(cancellationToken);
            Assert.Equal([1, 2, 3], settlements.Select(s => s.PeriodIndex));
            Assert.Equal([4.00m, 3.75m, 3.75m], settlements.Select(s => s.RatePercent));
            Assert.All(settlements, s => Assert.Equal(50, s.BondCount));
            Assert.Equal([16.50m, 15.50m, 15.50m], settlements.Select(s => s.GrossInterest));
            Assert.Equal([3.14m, 2.95m, 2.95m], settlements.Select(s => s.Tax));
            Assert.All(settlements, s => Assert.NotNull(s.CreditTransactionId));
            Assert.All(settlements, s => Assert.NotNull(s.TransferId));
        }

        var periodEnds = new[] { new DateOnly(2026, 7, 10), new DateOnly(2026, 8, 10), new DateOnly(2026, 9, 10) };
        var nets = new[] { 13.36m, 12.55m, 12.55m };

        var bondTransactions = (await client.ListTransactionsAsync(funded.BondPortfolioId, funded.Bond.AssetId, cancellationToken)).Items;
        var credits = bondTransactions.Where(t => t.BondInterestPeriodIndex is not null).OrderBy(t => t.BondInterestPeriodIndex).ToList();
        Assert.Equal(nets, credits.Select(t => t.Quantity));
        Assert.Equal(periodEnds, credits.Select(t => t.Date));
        Assert.All(credits, t => Assert.Equal(TransactionType.Deposit, t.Type));
        var bondLegs = bondTransactions
            .Where(t => t.Transfer is not null && t.Type == TransactionType.Withdraw)
            .OrderBy(t => t.Date)
            .ToList();
        Assert.Equal(nets, bondLegs.Select(t => t.Quantity));
        Assert.Equal(periodEnds, bondLegs.Select(t => t.Date));
        Assert.Equal(5_000m, (await client.GetAssetAsync(funded.BondPortfolioId, funded.Bond.AssetId, cancellationToken)).Quantity);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(funded.BondPortfolioId, funded.Bond.AssetId, cancellationToken);

        var cashIns = (await client.ListTransactionsAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken)).Items
            .Where(t => t.Transfer is not null && t.Type == TransactionType.Deposit)
            .OrderBy(t => t.Date)
            .ToList();
        Assert.Equal(nets, cashIns.Select(t => t.Quantity));
        Assert.Equal(periodEnds, cashIns.Select(t => t.Date));
        Assert.Equal(1_038.46m, (await client.GetAssetAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task Settle_CouponTaxExempt_PaysGross()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateFundedBondAsync(cancellationToken, request: NewRorBondRequest(taxExempt: true));

        var response = await client.SettleBondInterestRawAsync(
            funded.BondPortfolioId, funded.Bond.AssetId, [(1, null)], funded.CashAssetId, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var dbContext = CreateDbContext(userId);
        var settlement = await dbContext.Set<BondInterestSettlement>().SingleAsync(s => s.AssetId == funded.Bond.AssetId, cancellationToken);
        Assert.Equal(16.50m, settlement.GrossInterest);
        Assert.Equal(0m, settlement.Tax);
        Assert.Equal(1_016.50m, (await client.GetAssetAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken)).Quantity);
    }

    public static TheoryData<string, HttpStatusCode, string> InvalidRequests() => new()
    {
        { "skipped period", HttpStatusCode.Conflict, PortfolioAssertions.BondInterestPeriodMismatchErrorCode },
        { "period not yet ended", HttpStatusCode.Conflict, PortfolioAssertions.BondInterestNotDueErrorCode },
        { "missing rate", HttpStatusCode.BadRequest, PortfolioAssertions.BondPeriodRateErrorCode },
        { "rate sent for a fixed period", HttpStatusCode.BadRequest, PortfolioAssertions.BondPeriodRateErrorCode },
        { "coupon without destination", HttpStatusCode.BadRequest, PortfolioAssertions.BondPayoutDestinationRequiredErrorCode },
        { "capitalising with destination", HttpStatusCode.BadRequest, PortfolioAssertions.BondPayoutDestinationNotAllowedErrorCode },
        { "stock as destination", HttpStatusCode.BadRequest, PortfolioAssertions.InvalidTransferCounterpartErrorCode },
        { "archived bond", HttpStatusCode.Conflict, PortfolioAssertions.AssetArchivedErrorCode },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Settle_Invalid_IsRejected(string scenario, HttpStatusCode status, string errorCode)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var capitalising = scenario == "capitalising with destination";
        Factory.Clock.SetUtcNow(capitalising ? EdoYearOneEndedUtc : BondPurchaseDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateFundedBondAsync(
            cancellationToken, request: capitalising ? NewBondRequest() : NewRorBondRequest());
        var portfolioId = funded.BondPortfolioId;
        var assetId = funded.Bond.AssetId;
        (int PeriodIndex, decimal? RatePercent)[] periods = [(1, null)];
        Guid? destination = funded.CashAssetId;

        switch (scenario)
        {
            case "skipped period":
                periods = [(2, 3.75m)];
                break;
            case "period not yet ended":
                periods = [(1, null), (2, 3.75m), (3, 3.75m), (4, 3.75m)];
                break;
            case "missing rate":
                periods = [(1, null), (2, null)];
                break;
            case "rate sent for a fixed period":
                periods = [(1, 4.00m)];
                break;
            case "coupon without destination":
                destination = null;
                break;
            case "stock as destination":
                destination = await client.AddAssetAsync(await client.CreatePortfolioAsync(cancellationToken, name: "Stocks"), cancellationToken);
                break;
            case "archived bond":
                await client.ArchiveAssetAsync(portfolioId, assetId, cancellationToken);
                break;
        }

        var response = await client.SettleBondInterestRawAsync(portfolioId, assetId, periods, destination, cancellationToken);

        await response.AssertProblemAsync(status, errorCode, cancellationToken);
        Assert.Equal(0, await CountBondSettlementsAsync(userId, cancellationToken, assetId));
        Assert.Equal(1, (await client.ListTransactionsAsync(portfolioId, assetId, cancellationToken)).TotalCount);
        Assert.Equal(1_000m, (await client.GetAssetAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken)).Quantity);
    }
}
