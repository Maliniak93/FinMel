using System.Net;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class DeletePortfolioEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Delete_PortfolioWithNoAssets_ReturnsNoContentAndRemovesIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);

        var response = await client.DeleteAsync(PortfolioUri(portfolioId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var getAfterDelete = await client.GetAsync(PortfolioUri(portfolioId), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }

    /// <summary>spec-02 AC-13: the guard is now provable through the API alone (no counter to seed) — Assets.AnyAsync replaces the AssetCount check.</summary>
    [Fact]
    public async Task Delete_PortfolioContainingAssets_ReturnsConflictPointingToArchive()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        await client.AddAssetAsync(portfolioId, cancellationToken);

        var response = await client.DeleteAsync(PortfolioUri(portfolioId), cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("archive", problem, StringComparison.OrdinalIgnoreCase);

        var getAfterFailedDelete = await client.GetAsync(PortfolioUri(portfolioId), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, getAfterFailedDelete.StatusCode);
    }

    [Fact]
    public async Task Delete_NonExistentPortfolio_ReturnsNotFound()
    {
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.DeleteAsync(PortfolioUri(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
