using System.Net;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class UndoBondInterestSettlementEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Undo_LatestCoupon_RemovesRowCreditAndBothLegs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateFundedBondAsync(cancellationToken, request: NewRorBondRequest());
        var portfolioId = funded.BondPortfolioId;
        var assetId = funded.Bond.AssetId;
        await client.SettleBondInterestAsync(portfolioId, assetId, [(1, null), (2, 3.75m), (3, 3.75m)], funded.CashAssetId, cancellationToken);
        var latestId = await client.GetLastBondSettlementIdAsync(portfolioId, assetId, cancellationToken);

        var response = await client.DeleteAsync(BondInterestSettlementUri(portfolioId, assetId, latestId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(2, await CountBondSettlementsAsync(userId, cancellationToken, assetId));
        var bondTransactions = (await client.ListTransactionsAsync(portfolioId, assetId, cancellationToken)).Items;
        Assert.Equal([1, 2], bondTransactions.Where(t => t.BondInterestPeriodIndex is not null).Select(t => t.BondInterestPeriodIndex!.Value).Order());
        Assert.Equal(2, bondTransactions.Count(t => t.Transfer is not null && t.Type == TransactionType.Withdraw));
        Assert.Equal(5_000m, (await client.GetAssetAsync(portfolioId, assetId, cancellationToken)).Quantity);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(portfolioId, assetId, cancellationToken);
        var cashIns = (await client.ListTransactionsAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken)).Items
            .Where(t => t.Transfer is not null && t.Type == TransactionType.Deposit);
        Assert.Equal(2, cashIns.Count());
        Assert.Equal(1_025.91m, (await client.GetAssetAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken)).Quantity);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken);
        var bond = await client.GetBondAsync(portfolioId, assetId, cancellationToken);
        Assert.Equal(1, bond.DuePeriodCount);
    }

    [Fact]
    public async Task Undo_LatestCapitalising_RemovesRowAndCredit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(new DateTimeOffset(2027, 10, 2, 10, 0, 0, TimeSpan.Zero));
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, bond) = await client.CreatePortfolioWithBondAsync(cancellationToken);
        await client.SettleBondInterestAsync(portfolioId, bond.AssetId, [(1, null)], null, cancellationToken);
        var settlementId = await client.GetLastBondSettlementIdAsync(portfolioId, bond.AssetId, cancellationToken);

        var response = await client.DeleteAsync(BondInterestSettlementUri(portfolioId, bond.AssetId, settlementId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, await CountBondSettlementsAsync(userId, cancellationToken, bond.AssetId));
        Assert.Equal(5_000m, (await client.GetAssetAsync(portfolioId, bond.AssetId, cancellationToken)).Quantity);
        Assert.Equal(1, (await client.ListTransactionsAsync(portfolioId, bond.AssetId, cancellationToken)).TotalCount);
    }

    [Fact]
    public async Task Undo_NotLatest_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateFundedBondAsync(cancellationToken, request: NewRorBondRequest());
        var portfolioId = funded.BondPortfolioId;
        var assetId = funded.Bond.AssetId;
        await client.SettleBondInterestAsync(portfolioId, assetId, [(1, null)], funded.CashAssetId, cancellationToken);
        var firstId = await client.GetLastBondSettlementIdAsync(portfolioId, assetId, cancellationToken);
        await client.SettleBondInterestAsync(portfolioId, assetId, [(2, 3.75m)], funded.CashAssetId, cancellationToken);

        var response = await client.DeleteAsync(BondInterestSettlementUri(portfolioId, assetId, firstId), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.BondSettlementNotLatestErrorCode, cancellationToken);
        Assert.Equal(2, await CountBondSettlementsAsync(userId, cancellationToken, assetId));
        Assert.Equal(1_025.91m, (await client.GetAssetAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task Undo_CouponWhoseCashWasSpent_IsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateFundedBondAsync(cancellationToken, request: NewRorBondRequest());
        var portfolioId = funded.BondPortfolioId;
        var assetId = funded.Bond.AssetId;
        await client.SettleBondInterestAsync(portfolioId, assetId, [(1, null)], funded.CashAssetId, cancellationToken);
        var settlementId = await client.GetLastBondSettlementIdAsync(portfolioId, assetId, cancellationToken);
        await client.RecordTransactionAsync(
            funded.CashPortfolioId, funded.CashAssetId, TransactionType.Withdraw, 1_013.36m, new DateOnly(2026, 8, 1), cancellationToken, unitPrice: 1m);

        var response = await client.DeleteAsync(BondInterestSettlementUri(portfolioId, assetId, settlementId), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.OversellsPositionErrorCode, cancellationToken);
        Assert.Equal(1, await CountBondSettlementsAsync(userId, cancellationToken, assetId));
        Assert.Contains(
            (await client.ListTransactionsAsync(portfolioId, assetId, cancellationToken)).Items,
            t => t.BondInterestPeriodIndex == 1);
        Assert.Equal(0m, (await client.GetAssetAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task Undo_AfterEarlyRedemption_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(EdoEarlyRedemptionDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateEdoYearOneSettledAsync(cancellationToken);
        var portfolioId = funded.BondPortfolioId;
        var assetId = funded.Bond.AssetId;
        var settlementId = await client.GetLastBondSettlementIdAsync(portfolioId, assetId, cancellationToken);
        await client.RedeemBondEarlyAsync(
            portfolioId, assetId, EdoEarlyRedemptionDate, 4, funded.CashAssetId, cancellationToken, runningPeriodRatePercent: 4.00m);
        var rowsBefore = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.DeleteAsync(BondInterestSettlementUri(portfolioId, assetId, settlementId), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.BondRedeemedErrorCode, cancellationToken);
        Assert.Equal(1, await CountBondSettlementsAsync(userId, cancellationToken, assetId));
        Assert.Equal(rowsBefore, await SnapshotUserRowsAsync(userId, cancellationToken));
    }

    [Fact]
    public async Task Undo_Redeemed_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterTosMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateSettledTosAsync(cancellationToken);
        var portfolioId = funded.BondPortfolioId;
        var assetId = funded.Bond.AssetId;
        var latestId = await client.GetLastBondSettlementIdAsync(portfolioId, assetId, cancellationToken);
        await client.RedeemBondAsync(portfolioId, assetId, funded.CashAssetId, cancellationToken);
        var rowsBefore = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.DeleteAsync(BondInterestSettlementUri(portfolioId, assetId, latestId), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.BondRedeemedErrorCode, cancellationToken);
        Assert.Equal(3, await CountBondSettlementsAsync(userId, cancellationToken, assetId));
        Assert.Equal(rowsBefore, await SnapshotUserRowsAsync(userId, cancellationToken));
        Assert.Single(await ReadBondRedemptionsAsync(userId, cancellationToken, assetId));
    }
}
