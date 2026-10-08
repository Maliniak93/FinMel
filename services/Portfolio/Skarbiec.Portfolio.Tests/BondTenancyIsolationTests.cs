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
        await owner.CreatePortfolioWithBondAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await stranger.CreatePortfolioWithBondAsync(cancellationToken, NewBondRequest(name: "Stranger's own"));

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
        await owner.CreatePortfolioWithBondAsync(cancellationToken, NewEdoEarlyBondRequest());
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
        var (ownerPortfolioId, bond) = await owner.CreatePortfolioWithBondAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerPortfolioId = await stranger.CreatePortfolioAsync(cancellationToken);

        var viaOwnersPortfolio = await stranger.GetAsync(BondUri(ownerPortfolioId, bond.AssetId), cancellationToken);
        var viaStrangersPortfolio = await stranger.GetAsync(BondUri(strangerPortfolioId, bond.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, viaOwnersPortfolio.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, viaStrangersPortfolio.StatusCode);
    }

    [Fact]
    public async Task Put_ByStranger_ReturnsNotFoundAndLeavesBond()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var (ownerPortfolioId, bond) = await owner.CreatePortfolioWithBondAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerPortfolioId = await stranger.CreatePortfolioAsync(cancellationToken);
        var payload = NewBondRequest(name: "Stranger's edit", bondCount: 1).ToUpdateRequest();

        var viaOwnersPortfolio = await stranger.PutAsJsonAsync(BondUri(ownerPortfolioId, bond.AssetId), payload, cancellationToken);
        var viaStrangersPortfolio = await stranger.PutAsJsonAsync(BondUri(strangerPortfolioId, bond.AssetId), payload, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, viaOwnersPortfolio.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, viaStrangersPortfolio.StatusCode);
        var asset = await owner.GetAssetAsync(ownerPortfolioId, bond.AssetId, cancellationToken);
        Assert.Equal("EDO1036", asset.Name);
        Assert.Equal(5_000m, asset.Quantity);
        await using var dbContext = CreateDbContext(ownerId);
        Assert.Equal(50, (await dbContext.Set<TreasuryBond>().SingleAsync(t => t.AssetId == bond.AssetId, cancellationToken)).BondCount);
    }

    [Fact]
    public async Task Add_WithOwnersCashAsFundingSource_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var ownerWalletId = await owner.CreatePortfolioAsync(cancellationToken, name: "Wallet");
        var ownerCashId = await owner.AddCashAssetWithBalanceAsync(ownerWalletId, cancellationToken, balance: 6_000m);
        var strangerId = Guid.NewGuid();
        using var stranger = Factory.CreateAuthenticatedClient(strangerId);
        var strangerPortfolioId = await stranger.CreatePortfolioAsync(cancellationToken);

        var response = await stranger.PostAsJsonAsync(
            BondsUri(strangerPortfolioId), NewBondRequest(fundingAssetId: ownerCashId), cancellationToken);

        await response.AssertInvalidTransferCounterpartAsync(cancellationToken);
        await owner.AssertCashUntouchedAsync(ownerWalletId, ownerCashId, cancellationToken, balance: 6_000m);
        await using var dbContext = CreateDbContext(strangerId);
        Assert.Equal(0, await dbContext.Set<TreasuryBond>().CountAsync(cancellationToken));
        Assert.Equal(0, await dbContext.Assets.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Settle_ByStranger_ReturnsNotFoundAndWritesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var (ownerPortfolioId, bond) = await owner.CreatePortfolioWithBondAsync(cancellationToken, NewRorBondRequest());
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerPortfolioId = await stranger.CreatePortfolioAsync(cancellationToken);
        var strangerCashId = await stranger.AddCashAssetWithBalanceAsync(strangerPortfolioId, cancellationToken);

        var viaOwnersPortfolio = await stranger.SettleBondInterestRawAsync(ownerPortfolioId, bond.AssetId, [(1, null)], strangerCashId, cancellationToken);
        var viaStrangersPortfolio = await stranger.SettleBondInterestRawAsync(strangerPortfolioId, bond.AssetId, [(1, null)], strangerCashId, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, viaOwnersPortfolio.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, viaStrangersPortfolio.StatusCode);
        Assert.Equal(0, await CountBondSettlementsAsync(ownerId, cancellationToken, bond.AssetId));
        Assert.Equal(1, (await owner.ListTransactionsAsync(ownerPortfolioId, bond.AssetId, cancellationToken)).TotalCount);
    }

    [Fact]
    public async Task Preview_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (ownerPortfolioId, bond) = await owner.CreatePortfolioWithBondAsync(cancellationToken, NewRorBondRequest());
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerPortfolioId = await stranger.CreatePortfolioAsync(cancellationToken);
        var strangerCashId = await stranger.AddCashAssetWithBalanceAsync(strangerPortfolioId, cancellationToken);
        var body = NewSettleBondBody([(1, null)], strangerCashId);

        var viaOwnersPortfolio = await stranger.PostAsJsonAsync(BondInterestPreviewUri(ownerPortfolioId, bond.AssetId), body, cancellationToken);
        var viaStrangersPortfolio = await stranger.PostAsJsonAsync(BondInterestPreviewUri(strangerPortfolioId, bond.AssetId), body, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, viaOwnersPortfolio.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, viaStrangersPortfolio.StatusCode);
    }

    [Fact]
    public async Task Undo_ByStranger_ReturnsNotFoundAndLeavesSettlement()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var funded = await owner.CreateFundedBondAsync(cancellationToken, request: NewRorBondRequest());
        await owner.SettleBondInterestAsync(funded.BondPortfolioId, funded.Bond.AssetId, [(1, null)], funded.CashAssetId, cancellationToken);
        var settlementId = await owner.GetLastBondSettlementIdAsync(funded.BondPortfolioId, funded.Bond.AssetId, cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerPortfolioId = await stranger.CreatePortfolioAsync(cancellationToken);

        var viaOwnersPortfolio = await stranger.DeleteAsync(
            BondInterestSettlementUri(funded.BondPortfolioId, funded.Bond.AssetId, settlementId), cancellationToken);
        var viaStrangersPortfolio = await stranger.DeleteAsync(
            BondInterestSettlementUri(strangerPortfolioId, funded.Bond.AssetId, settlementId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, viaOwnersPortfolio.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, viaStrangersPortfolio.StatusCode);
        Assert.Equal(1, await CountBondSettlementsAsync(ownerId, cancellationToken, funded.Bond.AssetId));
        Assert.Equal(1_013.36m, (await owner.GetAssetAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken)).Quantity);
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
        var strangerPortfolioId = await stranger.CreatePortfolioAsync(cancellationToken);
        var strangerCashId = await stranger.AddCashAssetWithBalanceAsync(strangerPortfolioId, cancellationToken);
        var assetId = funded.Bond.AssetId;
        var rowsBefore = await SnapshotUserRowsAsync(ownerId, cancellationToken);

        foreach (var portfolioId in new[] { funded.BondPortfolioId, strangerPortfolioId })
        {
            var redeem = await stranger.RedeemBondRawAsync(portfolioId, assetId, strangerCashId, cancellationToken);
            var preview = await stranger.GetAsync(BondRedemptionPreviewUri(portfolioId, assetId), cancellationToken);
            var swap = await stranger.SwapBondRawAsync(portfolioId, assetId, NewSwapBody(10, strangerCashId), cancellationToken);

            Assert.Equal(HttpStatusCode.NotFound, redeem.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, swap.StatusCode);
        }

        Assert.Empty(await ReadBondRedemptionsAsync(ownerId, cancellationToken));
        Assert.Equal(rowsBefore, await SnapshotUserRowsAsync(ownerId, cancellationToken));
        await stranger.AssertCashUntouchedAsync(strangerPortfolioId, strangerCashId, cancellationToken);
    }

    [Fact]
    public async Task RedeemAndSwap_WithOwnersCashAsDestination_ReturnBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterTosMaturityUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var ownerWalletId = await owner.CreatePortfolioAsync(cancellationToken, name: "Wallet");
        var ownerCashId = await owner.AddCashAssetWithBalanceAsync(ownerWalletId, cancellationToken, balance: 6_000m);
        var strangerId = Guid.NewGuid();
        using var stranger = Factory.CreateAuthenticatedClient(strangerId);
        var funded = await stranger.CreateSettledTosAsync(cancellationToken);
        var rowsBefore = await SnapshotUserRowsAsync(strangerId, cancellationToken);

        var redeem = await stranger.RedeemBondRawAsync(funded.BondPortfolioId, funded.Bond.AssetId, ownerCashId, cancellationToken);
        var swap = await stranger.SwapBondRawAsync(
            funded.BondPortfolioId, funded.Bond.AssetId, NewSwapBody(10, ownerCashId), cancellationToken);

        await redeem.AssertInvalidTransferCounterpartAsync(cancellationToken);
        await swap.AssertInvalidTransferCounterpartAsync(cancellationToken);
        await owner.AssertCashUntouchedAsync(ownerWalletId, ownerCashId, cancellationToken, balance: 6_000m);
        Assert.Empty(await ReadBondRedemptionsAsync(strangerId, cancellationToken));
        Assert.Equal(rowsBefore, await SnapshotUserRowsAsync(strangerId, cancellationToken));
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
        var strangerPortfolioId = await stranger.CreatePortfolioAsync(cancellationToken);
        var strangerCashId = await stranger.AddCashAssetWithBalanceAsync(strangerPortfolioId, cancellationToken);
        var assetId = funded.Bond.AssetId;
        var rowsBefore = await SnapshotUserRowsAsync(ownerId, cancellationToken);

        foreach (var portfolioId in new[] { funded.BondPortfolioId, strangerPortfolioId })
        {
            var redeem = await stranger.RedeemBondEarlyRawAsync(
                portfolioId, assetId, EdoEarlyRedemptionDate, 4, strangerCashId, cancellationToken, runningPeriodRatePercent: 4.00m);
            var preview = await stranger.GetAsync(
                BondEarlyRedemptionPreviewUri(portfolioId, assetId, EdoEarlyRedemptionDate, 4, 4.00m), cancellationToken);

            Assert.Equal(HttpStatusCode.NotFound, redeem.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
        }

        Assert.Empty(await ReadBondRedemptionsAsync(ownerId, cancellationToken));
        Assert.Equal(rowsBefore, await SnapshotUserRowsAsync(ownerId, cancellationToken));
        await stranger.AssertCashUntouchedAsync(strangerPortfolioId, strangerCashId, cancellationToken);
    }

    [Fact]
    public async Task EarlyRedeem_WithOwnersCashAsDestination_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(EdoEarlyRedemptionDayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var ownerWalletId = await owner.CreatePortfolioAsync(cancellationToken, name: "Wallet");
        var ownerCashId = await owner.AddCashAssetWithBalanceAsync(ownerWalletId, cancellationToken, balance: 6_000m);
        var strangerId = Guid.NewGuid();
        using var stranger = Factory.CreateAuthenticatedClient(strangerId);
        var funded = await stranger.CreateEdoYearOneSettledAsync(cancellationToken);
        var rowsBefore = await SnapshotUserRowsAsync(strangerId, cancellationToken);

        var response = await stranger.RedeemBondEarlyRawAsync(
            funded.BondPortfolioId, funded.Bond.AssetId, EdoEarlyRedemptionDate, 4, ownerCashId, cancellationToken, runningPeriodRatePercent: 4.00m);

        await response.AssertInvalidTransferCounterpartAsync(cancellationToken);
        await owner.AssertCashUntouchedAsync(ownerWalletId, ownerCashId, cancellationToken, balance: 6_000m);
        Assert.Empty(await ReadBondRedemptionsAsync(strangerId, cancellationToken));
        Assert.Equal(rowsBefore, await SnapshotUserRowsAsync(strangerId, cancellationToken));
    }

    [Fact]
    public async Task Settle_WithOwnersCashAsDestination_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var ownerWalletId = await owner.CreatePortfolioAsync(cancellationToken, name: "Wallet");
        var ownerCashId = await owner.AddCashAssetWithBalanceAsync(ownerWalletId, cancellationToken, balance: 6_000m);
        var strangerId = Guid.NewGuid();
        using var stranger = Factory.CreateAuthenticatedClient(strangerId);
        var (strangerPortfolioId, bond) = await stranger.CreatePortfolioWithBondAsync(cancellationToken, NewRorBondRequest());

        var response = await stranger.SettleBondInterestRawAsync(strangerPortfolioId, bond.AssetId, [(1, null)], ownerCashId, cancellationToken);

        await response.AssertInvalidTransferCounterpartAsync(cancellationToken);
        await owner.AssertCashUntouchedAsync(ownerWalletId, ownerCashId, cancellationToken, balance: 6_000m);
        Assert.Equal(0, await CountBondSettlementsAsync(strangerId, cancellationToken, bond.AssetId));
    }
}
