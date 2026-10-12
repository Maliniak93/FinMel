using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Bonds;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class BondTenancyIsolationTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task List_ByStranger_DoesNotIncludeOwnersBond()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await owner.CreateAccountWithBondAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await stranger.CreateAccountWithBondAsync(cancellationToken, NewBondRequest(name: "Stranger's own"));

        var response = await stranger.GetAsync(AllBondsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var listed = Assert.Single((await response.Content.ReadFromJsonAsync<List<BondResponse>>(cancellationToken))!);
        Assert.Equal("Stranger's own", listed.Name);
    }

    [Fact]
    public async Task List_ForeignEstimates_Absent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(EdoEarlyRedemptionDayUtc);
        Factory.BondRateLookupClient.WithRates("EDO1036", (2, 4.00m));
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await owner.CreateAccountWithBondAsync(cancellationToken, NewEdoEarlyBondRequest());
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var ownerList = await owner.GetFromJsonAsync<List<BondResponse>>(AllBondsUri, cancellationToken);
        var strangerResponse = await stranger.GetAsync(AllBondsUri, cancellationToken);

        Assert.NotNull(Assert.Single(ownerList!).Estimate);
        Assert.Equal(HttpStatusCode.OK, strangerResponse.StatusCode);
        Assert.Empty((await strangerResponse.Content.ReadFromJsonAsync<List<BondResponse>>(cancellationToken))!);
        Assert.Single(Factory.BondRateLookupClient.Calls);
    }

    [Fact]
    public async Task Get_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (ownerAccountId, bond) = await owner.CreateAccountWithBondAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await stranger.GetAsync(BondUri(bond.AssetId), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_ByStranger_ReturnsNotFoundAndLeavesBond()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var (ownerAccountId, bond) = await owner.CreateAccountWithBondAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var payload = NewBondRequest(name: "Stranger's edit", bondCount: 1).ToUpdateRequest();

        var response = await stranger.PutAsJsonAsync(BondUri(bond.AssetId), payload, cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var asset = await owner.GetAssetAsync(bond.AssetId, cancellationToken);
        Assert.Equal("EDO1036", asset.Name);
        Assert.Equal(5_000m, asset.Quantity);
        await using var dbContext = CreateDbContext(ownerId);
        Assert.Equal(50, (await dbContext.Set<TreasuryBond>().SingleAsync(t => t.AssetId == bond.AssetId, cancellationToken)).BondCount);
    }

    [Fact]
    public async Task Settle_ByStranger_ReturnsNotFoundAndWritesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var (ownerAccountId, bond) = await owner.CreateAccountWithBondAsync(cancellationToken, NewRorBondRequest());
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await stranger.SettleBondInterestRawAsync(bond.AssetId, [(1, null)], cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await CountBondSettlementsAsync(ownerId, cancellationToken, bond.AssetId));
        Assert.Equal(1, (await owner.ListTransactionsAsync(bond.AssetId, cancellationToken)).TotalCount);
    }

    [Fact]
    public async Task Preview_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (ownerAccountId, bond) = await owner.CreateAccountWithBondAsync(cancellationToken, NewRorBondRequest());
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var body = NewSettleBondBody([(1, null)]);

        var response = await stranger.PostAsJsonAsync(BondInterestPreviewUri(bond.AssetId), body, cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Undo_ByStranger_ReturnsNotFoundAndLeavesSettlement()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var funded = await owner.CreateFundedBondAsync(cancellationToken, request: NewRorBondRequest());
        await owner.SettleBondInterestAsync(funded.Bond.AssetId, [(1, null)], cancellationToken);
        var settlementId = await owner.GetLastBondSettlementIdAsync(funded.Bond.AssetId, cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await stranger.DeleteAsync(
            BondInterestSettlementUri(funded.Bond.AssetId, settlementId), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, await CountBondSettlementsAsync(ownerId, cancellationToken, funded.Bond.AssetId));
        Assert.Equal(1_013.36m, (await owner.GetAssetAsync(funded.CashAssetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task RedeemPreviewAndSwap_ByStranger_ReturnNotFoundAndWriteNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterTosMaturityUtc);
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var funded = await owner.CreateSettledTosAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerCashId = await stranger.AddCashAssetWithBalanceAsync(cancellationToken);
        var assetId = funded.Bond.AssetId;
        var rowsBefore = await SnapshotUserRowsAsync(ownerId, cancellationToken);

        var redeem = await stranger.RedeemBondRawAsync(assetId, cancellationToken);
        var preview = await stranger.GetAsync(BondRedemptionPreviewUri(assetId), cancellationToken);
        var swap = await stranger.SwapBondRawAsync(assetId, NewSwapBody(10), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, redeem.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, swap.StatusCode);

        Assert.Empty(await ReadBondRedemptionsAsync(ownerId, cancellationToken));
        Assert.Equal(rowsBefore, await SnapshotUserRowsAsync(ownerId, cancellationToken));
        await stranger.AssertCashUntouchedAsync(strangerCashId, cancellationToken);
    }

    [Fact]
    public async Task EarlyRedeemAndPreview_ByStranger_ReturnNotFoundAndWriteNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(EdoEarlyRedemptionDayUtc);
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var funded = await owner.CreateEdoYearOneSettledAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerCashId = await stranger.AddCashAssetWithBalanceAsync(cancellationToken);
        var assetId = funded.Bond.AssetId;
        var rowsBefore = await SnapshotUserRowsAsync(ownerId, cancellationToken);

        var redeem = await stranger.RedeemBondEarlyRawAsync(
            assetId, EdoEarlyRedemptionDate, 4, cancellationToken, runningPeriodRatePercent: 4.00m);
        var preview = await stranger.GetAsync(
            BondEarlyRedemptionPreviewUri(assetId, EdoEarlyRedemptionDate, 4, 4.00m), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, redeem.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);

        Assert.Empty(await ReadBondRedemptionsAsync(ownerId, cancellationToken));
        Assert.Equal(rowsBefore, await SnapshotUserRowsAsync(ownerId, cancellationToken));
        await stranger.AssertCashUntouchedAsync(strangerCashId, cancellationToken);
    }
}
