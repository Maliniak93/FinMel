using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Bonds;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
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
}
