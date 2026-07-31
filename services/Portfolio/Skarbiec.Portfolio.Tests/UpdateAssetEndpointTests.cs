using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.CreatePortfolio;
using Skarbiec.Portfolio.Features.UpdateAsset;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class UpdateAssetEndpointTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private const string PortfoliosUri = "/api/portfolio/portfolios";

    private readonly PortfolioApiFactory _factory = new(containers);

    public ValueTask InitializeAsync() => new(_factory.ResetDatabaseAsync());

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Update_ExistingAsset_ReturnsOkWithUpdatedFields()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await CreatePortfolioWithAssetAsync(client, cancellationToken);
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Crypto,
            Name = "Renamed",
            Currency = "USD",
            Quantity = 3m,
            ManualValue = 42.42m,
            ManualValueDate = new DateOnly(2026, 6, 15)
        };

        var response = await client.PutAsJsonAsync($"{PortfoliosUri}/{portfolioId}/assets/{assetId}", request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal(AssetClass.Crypto, body!.AssetClass);
        Assert.Equal("Renamed", body.Name);
        Assert.Equal("USD", body.Currency);
        Assert.Equal(3m, body.Quantity);
        Assert.Equal(42.42m, body.ManualValue);
        Assert.Equal(new DateOnly(2026, 6, 15), body.ManualValueDate);
    }

    [Fact]
    public async Task Update_WithNegativeManualValue_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await CreatePortfolioWithAssetAsync(client, cancellationToken);
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Still here",
            Currency = "PLN",
            ManualValue = -10m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PutAsJsonAsync($"{PortfoliosUri}/{portfolioId}/assets/{assetId}", request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_NonExistentAsset_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await CreatePortfolioAsync(client, cancellationToken);
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Missing",
            Currency = "PLN",
            ManualValue = 1m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PutAsJsonAsync($"{PortfoliosUri}/{portfolioId}/assets/{Guid.NewGuid()}", request, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_AssetUnderWrongPortfolio_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (_, assetId) = await CreatePortfolioWithAssetAsync(client, cancellationToken);
        var otherPortfolioId = await CreatePortfolioAsync(client, cancellationToken, "Other portfolio");
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Wrong parent",
            Currency = "PLN",
            ManualValue = 1m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PutAsJsonAsync($"{PortfoliosUri}/{otherPortfolioId}/assets/{assetId}", request, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<Guid> CreatePortfolioAsync(HttpClient client, CancellationToken cancellationToken, string name = "Retirement")
    {
        var response = await client.PostAsJsonAsync(
            PortfoliosUri, new CreatePortfolioRequest { Name = name, Currency = "PLN" }, cancellationToken);
        var portfolio = await response.Content.ReadFromJsonAsync<PortfolioResponse>(cancellationToken);
        return portfolio!.Id;
    }

    private static async Task<(Guid PortfolioId, Guid AssetId)> CreatePortfolioWithAssetAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var portfolioId = await CreatePortfolioAsync(client, cancellationToken);
        var addRequest = new AddAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Original",
            Currency = "PLN",
            ManualValue = 100m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };
        var response = await client.PostAsJsonAsync($"{PortfoliosUri}/{portfolioId}/assets", addRequest, cancellationToken);
        var asset = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        return (portfolioId, asset!.Id);
    }
}
