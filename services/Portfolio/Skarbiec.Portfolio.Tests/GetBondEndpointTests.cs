using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Bonds;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class GetBondEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Get_RorWithEndedPeriods_ReturnsDuePeriodsAndInterestDue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var request = NewBondRequest(
            name: "ROR0627",
            seriesCode: "ROR0627",
            type: TreasuryBondType.Ror,
            purchaseDate: new DateOnly(2026, 6, 10),
            marginPercent: null);
        var (portfolioId, bond) = await client.CreatePortfolioWithBondAsync(cancellationToken, request);

        var response = await client.GetAsync(BondUri(portfolioId, bond.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BondResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(new DateOnly(2027, 6, 10), body.MaturityDate);
        Assert.Equal(BondStatus.InterestDue, body.Status);
        Assert.Equal(12, body.Periods.Count);
        Assert.Equal(new DateOnly(2026, 7, 10), body.Periods[0].End);
        Assert.All(body.Periods.Take(3), p => Assert.Equal(BondPeriodState.Due, p.State));
        Assert.All(body.Periods.Skip(3), p => Assert.Equal(BondPeriodState.Upcoming, p.State));
    }

    [Fact]
    public async Task Get_PastMaturity_ReturnsMatured()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var request = NewBondRequest(
            name: "OTS0926",
            seriesCode: "OTS0926",
            type: TreasuryBondType.Ots,
            purchaseDate: new DateOnly(2026, 6, 10),
            marginPercent: null);
        var (portfolioId, bond) = await client.CreatePortfolioWithBondAsync(cancellationToken, request);

        var body = await client.GetBondAsync(portfolioId, bond.AssetId, cancellationToken);

        Assert.Equal(new DateOnly(2026, 9, 10), body.MaturityDate);
        Assert.Equal(BondStatus.Matured, body.Status);
    }

    [Fact]
    public async Task Get_NonBondAsset_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, bond) = await client.CreatePortfolioWithBondAsync(cancellationToken);
        var cashId = await client.AddCashAssetAsync(portfolioId, cancellationToken);

        var response = await client.GetAsync(BondUri(portfolioId, cashId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(BondUri(portfolioId, bond.AssetId), cancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Get_AfterSettlement_ShowsSettledPeriod()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var funded = await client.CreateFundedBondAsync(cancellationToken, request: NewRorBondRequest());
        await client.SettleBondInterestAsync(
            funded.BondPortfolioId, funded.Bond.AssetId, [(1, null), (2, 3.75m)], funded.CashAssetId, cancellationToken);

        var body = await client.GetBondAsync(funded.BondPortfolioId, funded.Bond.AssetId, cancellationToken);

        Assert.Equal(BondPeriodState.Settled, body.Periods[0].State);
        Assert.Equal(BondPeriodState.Settled, body.Periods[1].State);
        Assert.Equal(BondPeriodState.Due, body.Periods[2].State);
        Assert.Equal(BondPeriodState.Upcoming, body.Periods[3].State);
        var first = body.Periods[0].Settlement!;
        Assert.Equal(4.00m, first.RatePercent);
        Assert.Equal(50, first.BondCount);
        Assert.Equal(16.50m, first.GrossInterest);
        Assert.Equal(3.14m, first.Tax);
        Assert.Equal(3.75m, body.Periods[1].Settlement!.RatePercent);
        Assert.Null(body.Periods[2].Settlement);
        Assert.Equal(1, body.DuePeriodCount);
        Assert.Equal(BondStatus.InterestDue, body.Status);
        Assert.Equal(body.Periods[1].Settlement!.SettlementId, body.LastSettlement!.SettlementId);
    }

    [Fact]
    public async Task Get_EarlyRedeemed_ShowsRedemption()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(EdoEarlyRedemptionDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var funded = await client.CreateEdoYearOneSettledAsync(cancellationToken);
        await client.RedeemBondEarlyAsync(
            funded.BondPortfolioId, funded.Bond.AssetId, EdoEarlyRedemptionDate, 4, funded.CashAssetId, cancellationToken, runningPeriodRatePercent: 4.00m);

        var bond = await client.GetBondAsync(funded.BondPortfolioId, funded.Bond.AssetId, cancellationToken);

        Assert.Equal(6, bond.BondCount);
        var redemption = Assert.Single(bond.Redemptions);
        Assert.Equal("Early", redemption.Kind.ToString());
        Assert.Equal(EdoEarlyRedemptionDate, redemption.Date);
        Assert.Equal(4, redemption.BondCount);
        Assert.Equal(24.76m, redemption.AccruedInterest);
        Assert.Equal(12.00m, redemption.Fee);
        Assert.Equal(2.43m, redemption.Tax);
        Assert.Equal(410.33m, redemption.Proceeds);
        Assert.Equal("Cash account", redemption.DestinationAssetName);
        Assert.Null(redemption.SwapTargetAssetName);
    }

    [Fact]
    public async Task Get_Redeemed_ShowsRedemptionAndSwapLinks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterTosMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var funded = await client.CreateSettledTosAsync(cancellationToken);
        await client.SwapBondAsync(funded.BondPortfolioId, funded.Bond.AssetId, 6, funded.CashAssetId, cancellationToken);

        var tos = await client.GetBondAsync(funded.BondPortfolioId, funded.Bond.AssetId, cancellationToken);
        var edo = await client.FindSwappedBondAsync(funded.Bond.AssetId, cancellationToken);

        Assert.Equal(BondStatus.Redeemed, tos.Status);
        Assert.Equal(0m, tos.BookValue);
        Assert.Null(tos.SwappedFrom);
        var redemption = Assert.Single(tos.Redemptions);
        Assert.Equal("Swap", redemption.Kind.ToString());
        Assert.Equal(TosMaturityDate, redemption.Date);
        Assert.Equal(10, redemption.BondCount);
        Assert.Equal(26.21m, redemption.Tax);
        Assert.Equal(1_111.69m, redemption.Proceeds);
        Assert.Equal("Cash account", redemption.DestinationAssetName);
        Assert.Equal("EDO1036", redemption.SwapTargetAssetName);
        Assert.Equal(BondStatus.Active, edo.Status);
        Assert.Equal(funded.Bond.AssetId, edo.SwappedFrom!.AssetId);
        Assert.Equal("TOS1029", edo.SwappedFrom.Name);
        Assert.Empty(edo.Redemptions);
    }

    [Fact]
    public async Task Get_MarketDataDown_EstimateNull()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(EdoEarlyRedemptionDayUtc);
        Factory.BondRateLookupClient.WithUnavailable();
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, bond) = await client.CreatePortfolioWithBondAsync(cancellationToken, NewEdoEarlyBondRequest());

        var response = await client.GetAsync(BondUri(portfolioId, bond.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BondResponse>(cancellationToken);
        Assert.Null(body!.Estimate);
        Assert.Equal(BondEstimateUnavailableReason.MarketDataUnavailable, body.EstimateUnavailableReason);
    }
}
