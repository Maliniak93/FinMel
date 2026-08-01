using System.Net;
using Microsoft.EntityFrameworkCore;
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

    [Fact]
    public async Task Delete_PortfolioContainingAssets_ReturnsConflictPointingToArchive()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(ownerId);
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);

        // Asset doesn't exist as an entity until T1.2 (which depends on this task) — AssetCount is
        // the denormalized stand-in T1.2's AddAsset/RemoveAsset will maintain. Seeded directly here
        // since there's no API to create a real asset yet.
        await BumpAssetCountAsync(ownerId, portfolioId, cancellationToken);

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

    private async Task BumpAssetCountAsync(Guid ownerId, Guid portfolioId, CancellationToken cancellationToken)
    {
        await using var dbContext = CreateDbContext(ownerId);
        var portfolio = await dbContext.Portfolios.SingleAsync(p => p.Id == portfolioId, cancellationToken);
        portfolio.AssetCount = 1;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
