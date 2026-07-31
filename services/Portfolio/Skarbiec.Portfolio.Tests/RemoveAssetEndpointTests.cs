using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.CreatePortfolio;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class RemoveAssetEndpointTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private const string PortfoliosUri = "/api/portfolio/portfolios";

    private readonly PortfolioApiFactory _factory = new(containers);

    public ValueTask InitializeAsync() => new(_factory.ResetDatabaseAsync());

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Remove_AssetWithNoTransactions_ReturnsNoContentAndRemovesIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await CreatePortfolioWithAssetAsync(client, cancellationToken);

        var response = await client.DeleteAsync($"{PortfoliosUri}/{portfolioId}/assets/{assetId}", cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var getAfterDelete = await client.GetAsync($"{PortfoliosUri}/{portfolioId}/assets/{assetId}", cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }

    [Fact]
    public async Task Remove_LastAsset_UnblocksPortfolioDelete()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await CreatePortfolioWithAssetAsync(client, cancellationToken);

        await client.DeleteAsync($"{PortfoliosUri}/{portfolioId}/assets/{assetId}", cancellationToken);
        var deletePortfolio = await client.DeleteAsync($"{PortfoliosUri}/{portfolioId}", cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, deletePortfolio.StatusCode);
    }

    [Fact]
    public async Task Remove_AssetWithTransactions_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(ownerId);
        var (portfolioId, assetId) = await CreatePortfolioWithAssetAsync(client, cancellationToken);

        // Transaction doesn't exist as an entity until T1.3 (which depends on this task) —
        // TransactionCount is the denormalized stand-in T1.3's RecordTransaction will maintain.
        // Seeded directly here since there's no API to record a real transaction yet.
        await BumpTransactionCountAsync(ownerId, assetId, cancellationToken);

        var response = await client.DeleteAsync($"{PortfoliosUri}/{portfolioId}/assets/{assetId}", cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var getAfterFailedDelete = await client.GetAsync($"{PortfoliosUri}/{portfolioId}/assets/{assetId}", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, getAfterFailedDelete.StatusCode);
    }

    [Fact]
    public async Task Remove_NonExistentAsset_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioResponse = await client.PostAsJsonAsync(
            PortfoliosUri, new CreatePortfolioRequest { Name = "Retirement", Currency = "PLN" }, cancellationToken);
        var portfolio = await portfolioResponse.Content.ReadFromJsonAsync<PortfolioResponse>(cancellationToken);

        var response = await client.DeleteAsync($"{PortfoliosUri}/{portfolio!.Id}/assets/{Guid.NewGuid()}", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<(Guid PortfolioId, Guid AssetId)> CreatePortfolioWithAssetAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var portfolioResponse = await client.PostAsJsonAsync(
            PortfoliosUri, new CreatePortfolioRequest { Name = "Retirement", Currency = "PLN" }, cancellationToken);
        var portfolio = await portfolioResponse.Content.ReadFromJsonAsync<PortfolioResponse>(cancellationToken);
        var addRequest = new AddAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Removable",
            Currency = "PLN",
            ManualValue = 100m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };
        var addResponse = await client.PostAsJsonAsync($"{PortfoliosUri}/{portfolio!.Id}/assets", addRequest, cancellationToken);
        var asset = await addResponse.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        return (portfolio.Id, asset!.Id);
    }

    private async Task BumpTransactionCountAsync(Guid ownerId, Guid assetId, CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<PortfolioDbContext>()
            .UseNpgsql(containers.PostgresConnectionString)
            .Options;

        await using var dbContext = new PortfolioDbContext(options, new StubCurrentUser(ownerId));
        var asset = await dbContext.Assets.SingleAsync(a => a.Id == assetId, cancellationToken);
        asset.TransactionCount = 1;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
