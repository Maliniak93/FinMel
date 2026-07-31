using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.CreatePortfolio;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class ListAssetsEndpointTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private const string PortfoliosUri = "/api/portfolio/portfolios";

    private readonly PortfolioApiFactory _factory = new(containers);

    public ValueTask InitializeAsync() => new(_factory.ResetDatabaseAsync());

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task List_ReturnsOnlyAssetsOfThatPortfolio()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioA = await CreatePortfolioAsync(client, cancellationToken, "A");
        var portfolioB = await CreatePortfolioAsync(client, cancellationToken, "B");
        await AddAssetAsync(client, portfolioA, "Asset in A", cancellationToken);
        await AddAssetAsync(client, portfolioB, "Asset in B", cancellationToken);

        var response = await client.GetAsync($"{PortfoliosUri}/{portfolioA}/assets", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var assets = await response.Content.ReadFromJsonAsync<List<AssetResponse>>(cancellationToken);
        var asset = Assert.Single(assets!);
        Assert.Equal("Asset in A", asset.Name);
    }

    [Fact]
    public async Task List_PortfolioWithNoAssets_ReturnsEmptyList()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await CreatePortfolioAsync(client, cancellationToken);

        var response = await client.GetAsync($"{PortfoliosUri}/{portfolioId}/assets", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var assets = await response.Content.ReadFromJsonAsync<List<AssetResponse>>(cancellationToken);
        Assert.Empty(assets!);
    }

    [Fact]
    public async Task List_NonExistentPortfolio_ReturnsNotFound()
    {
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync($"{PortfoliosUri}/{Guid.NewGuid()}/assets", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<Guid> CreatePortfolioAsync(HttpClient client, CancellationToken cancellationToken, string name = "Retirement")
    {
        var response = await client.PostAsJsonAsync(
            PortfoliosUri, new CreatePortfolioRequest { Name = name, Currency = "PLN" }, cancellationToken);
        var portfolio = await response.Content.ReadFromJsonAsync<PortfolioResponse>(cancellationToken);
        return portfolio!.Id;
    }

    private static async Task AddAssetAsync(HttpClient client, Guid portfolioId, string name, CancellationToken cancellationToken)
    {
        var request = new AddAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = name,
            Currency = "PLN",
            ManualValue = 100m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };
        await client.PostAsJsonAsync($"{PortfoliosUri}/{portfolioId}/assets", request, cancellationToken);
    }
}
