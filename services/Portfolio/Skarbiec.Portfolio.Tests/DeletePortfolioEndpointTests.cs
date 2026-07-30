using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.CreatePortfolio;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class DeletePortfolioEndpointTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private const string PortfoliosUri = "/api/portfolio/portfolios";

    private readonly PortfolioApiFactory _factory = new(containers);

    public ValueTask InitializeAsync() => new(_factory.ResetDatabaseAsync());

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Delete_PortfolioWithNoAssets_ReturnsNoContentAndRemovesIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var created = await client.PostAsJsonAsync(
            PortfoliosUri, new CreatePortfolioRequest { Name = "Retirement", Currency = "PLN" }, cancellationToken);
        var portfolio = await created.Content.ReadFromJsonAsync<PortfolioResponse>(cancellationToken);

        var response = await client.DeleteAsync($"{PortfoliosUri}/{portfolio!.Id}", cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var getAfterDelete = await client.GetAsync($"{PortfoliosUri}/{portfolio.Id}", cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }

    [Fact]
    public async Task Delete_PortfolioContainingAssets_ReturnsConflictPointingToArchive()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(ownerId);
        var created = await client.PostAsJsonAsync(
            PortfoliosUri, new CreatePortfolioRequest { Name = "Retirement", Currency = "PLN" }, cancellationToken);
        var portfolio = await created.Content.ReadFromJsonAsync<PortfolioResponse>(cancellationToken);

        // Asset doesn't exist as an entity until T1.2 (which depends on this task) — AssetCount is
        // the denormalized stand-in T1.2's AddAsset/RemoveAsset will maintain. Seeded directly here
        // since there's no API to create a real asset yet.
        await BumpAssetCountAsync(ownerId, portfolio!.Id, cancellationToken);

        var response = await client.DeleteAsync($"{PortfoliosUri}/{portfolio.Id}", cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("archive", problem, StringComparison.OrdinalIgnoreCase);

        var getAfterFailedDelete = await client.GetAsync($"{PortfoliosUri}/{portfolio.Id}", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, getAfterFailedDelete.StatusCode);
    }

    [Fact]
    public async Task Delete_NonExistentPortfolio_ReturnsNotFound()
    {
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.DeleteAsync($"{PortfoliosUri}/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task BumpAssetCountAsync(Guid ownerId, Guid portfolioId, CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<PortfolioDbContext>()
            .UseNpgsql(containers.PostgresConnectionString)
            .Options;

        await using var dbContext = new PortfolioDbContext(options, new StubCurrentUser(ownerId));
        var portfolio = await dbContext.Portfolios.SingleAsync(p => p.Id == portfolioId, cancellationToken);
        portfolio.AssetCount = 1;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
