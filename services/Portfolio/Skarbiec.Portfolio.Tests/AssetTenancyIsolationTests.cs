using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.UpdateAsset;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

/// <summary>
/// T1.6: Asset is nested under Portfolio (<c>.../portfolios/{portfolioId}/assets/{id}</c>), so it
/// can't plug into the flat T0.14 <see cref="Skarbiec.Testing.Tenancy.TenancyIsolationTests{TProgram}"/>
/// template directly (its <c>ListUrl</c> has no portfolio to scope under) — the same four facts are
/// reimplemented here, plus the sneaky-path case the plain template can't express: a stranger
/// addressing another user's asset underneath the stranger's own, real portfolio id.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class AssetTenancyIsolationTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Get_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (ownerPortfolioId, assetId) = await owner.CreatePortfolioWithAssetAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await stranger.GetAsync(AssetUri(ownerPortfolioId, assetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (ownerPortfolioId, assetId) = await owner.CreatePortfolioWithAssetAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await stranger.PutAsJsonAsync(AssetUri(ownerPortfolioId, assetId), UpdatePayload(), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (ownerPortfolioId, assetId) = await owner.CreatePortfolioWithAssetAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await stranger.DeleteAsync(AssetUri(ownerPortfolioId, assetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_ForStrangersOwnPortfolio_DoesNotIncludeResource()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await owner.CreatePortfolioWithAssetAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerPortfolioId = await stranger.CreatePortfolioAsync(cancellationToken);

        var response = await stranger.GetAsync(AssetsUri(strangerPortfolioId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var assets = await response.Content.ReadFromJsonAsync<List<AssetResponse>>(cancellationToken);
        Assert.Empty(assets!);
    }

    [Fact]
    public async Task Get_AssetUnderStrangersOwnPortfolio_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (_, assetId) = await owner.CreatePortfolioWithAssetAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerPortfolioId = await stranger.CreatePortfolioAsync(cancellationToken);

        var response = await stranger.GetAsync(AssetUri(strangerPortfolioId, assetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_AssetUnderStrangersOwnPortfolio_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (_, assetId) = await owner.CreatePortfolioWithAssetAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerPortfolioId = await stranger.CreatePortfolioAsync(cancellationToken);

        var response = await stranger.PutAsJsonAsync(AssetUri(strangerPortfolioId, assetId), UpdatePayload(), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_AssetUnderStrangersOwnPortfolio_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (_, assetId) = await owner.CreatePortfolioWithAssetAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerPortfolioId = await stranger.CreatePortfolioAsync(cancellationToken);

        var response = await stranger.DeleteAsync(AssetUri(strangerPortfolioId, assetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static UpdateAssetRequest UpdatePayload() => new()
    {
        AssetClass = AssetClass.Cash,
        Name = "Stranger's edit",
        Currency = "PLN",
        ManualValue = 1m,
        ManualValueDate = new DateOnly(2026, 1, 1)
    };
}
