using System.Net;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class AssetArchiveTenancyIsolationTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Archive_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await owner.CreatePortfolioWithAssetAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await stranger.PostAsync(ArchiveAssetUri(assetId), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False((await owner.GetAssetAsync(assetId, cancellationToken)).IsArchived);
    }

    [Fact]
    public async Task Restore_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await owner.CreatePortfolioWithAssetAsync(cancellationToken);
        await owner.ArchiveAssetAsync(assetId, cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await stranger.PostAsync(RestoreAssetUri(assetId), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True((await owner.GetAssetAsync(assetId, cancellationToken)).IsArchived);
    }

    [Fact]
    public async Task Archive_AssetUnderStrangersOwnPortfolio_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (ownerPortfolioId, assetId) = await owner.CreatePortfolioWithAssetAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerPortfolioId = await stranger.CreatePortfolioAsync(cancellationToken);
        var response = await stranger.PostAsync(ArchiveAssetUri(assetId), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False((await owner.GetAssetAsync(assetId, cancellationToken)).IsArchived);
    }

    [Fact]
    public async Task Restore_AssetUnderStrangersOwnPortfolio_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (ownerPortfolioId, assetId) = await owner.CreatePortfolioWithAssetAsync(cancellationToken);
        await owner.ArchiveAssetAsync(assetId, cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerPortfolioId = await stranger.CreatePortfolioAsync(cancellationToken);
        var response = await stranger.PostAsync(RestoreAssetUri(assetId), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True((await owner.GetAssetAsync(assetId, cancellationToken)).IsArchived);
    }
}
