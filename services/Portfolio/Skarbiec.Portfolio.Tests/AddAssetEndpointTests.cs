using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.CreatePortfolio;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class AddAssetEndpointTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private const string PortfoliosUri = "/api/portfolio/portfolios";

    private readonly PortfolioApiFactory _factory = new(containers);

    public ValueTask InitializeAsync() => new(_factory.ResetDatabaseAsync());

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Theory]
    [InlineData(AssetClass.Cash)]
    [InlineData(AssetClass.Deposit)]
    [InlineData(AssetClass.Stock)]
    [InlineData(AssetClass.Etf)]
    [InlineData(AssetClass.Bond)]
    [InlineData(AssetClass.Crypto)]
    [InlineData(AssetClass.PreciousMetal)]
    [InlineData(AssetClass.RealEstate)]
    [InlineData(AssetClass.Other)]
    public async Task Add_EveryAssetClassWithManualValue_ReturnsCreated(AssetClass assetClass)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await CreatePortfolioAsync(client, cancellationToken);
        var request = new AddAssetRequest
        {
            AssetClass = assetClass,
            Name = "Test asset",
            Currency = "USD",
            Quantity = 2.5m,
            ManualValue = 1000.50m,
            ManualValueDate = new DateOnly(2026, 7, 1)
        };

        var response = await client.PostAsJsonAsync($"{PortfoliosUri}/{portfolioId}/assets", request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(assetClass, body.AssetClass);
        Assert.Equal("USD", body.Currency);
        Assert.Equal(2.5m, body.Quantity);
        Assert.Equal(1000.50m, body.ManualValue);
        Assert.Equal(new DateOnly(2026, 7, 1), body.ManualValueDate);
        Assert.Equal($"{PortfoliosUri}/{portfolioId}/assets/{body.Id}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Add_WithoutQuantity_DefaultsToZero()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await CreatePortfolioAsync(client, cancellationToken);
        var request = new { AssetClass = AssetClass.RealEstate, Name = "Flat", ManualValue = 500000m, ManualValueDate = new DateOnly(2026, 1, 1) };

        var response = await client.PostAsJsonAsync($"{PortfoliosUri}/{portfolioId}/assets", request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal(0m, body!.Quantity);
        Assert.Equal("PLN", body.Currency);
    }

    [Fact]
    public async Task Add_WithNegativeManualValue_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await CreatePortfolioAsync(client, cancellationToken);
        var request = new AddAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Negative value",
            Currency = "PLN",
            ManualValue = -1m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PostAsJsonAsync($"{PortfoliosUri}/{portfolioId}/assets", request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Add_WithNegativeQuantity_ReturnsBadRequestWithFieldDetails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await CreatePortfolioAsync(client, cancellationToken);
        var request = new AddAssetRequest
        {
            AssetClass = AssetClass.Stock,
            Name = "Negative quantity",
            Currency = "PLN",
            Quantity = -5m,
            ManualValue = 100m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PostAsJsonAsync($"{PortfoliosUri}/{portfolioId}/assets", request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);
        Assert.NotNull(problem);
        Assert.Contains(problem.Errors, e => e.Key.Equals(nameof(AddAssetRequest.Quantity), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Add_ForNonExistentPortfolio_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var request = new AddAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Orphan",
            Currency = "PLN",
            ManualValue = 1m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PostAsJsonAsync($"{PortfoliosUri}/{Guid.NewGuid()}/assets", request, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Add_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();
        var request = new AddAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Unauthorized",
            Currency = "PLN",
            ManualValue = 1m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PostAsJsonAsync($"{PortfoliosUri}/{Guid.NewGuid()}/assets", request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Add_AssetToPortfolio_MakesPortfolioDeleteConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await CreatePortfolioAsync(client, cancellationToken);
        var request = new AddAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Blocks portfolio delete",
            Currency = "PLN",
            ManualValue = 1m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };
        await client.PostAsJsonAsync($"{PortfoliosUri}/{portfolioId}/assets", request, cancellationToken);

        var deleteResponse = await client.DeleteAsync($"{PortfoliosUri}/{portfolioId}", cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);
    }

    private static async Task<Guid> CreatePortfolioAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync(
            PortfoliosUri, new CreatePortfolioRequest { Name = "Retirement", Currency = "PLN" }, cancellationToken);
        var portfolio = await response.Content.ReadFromJsonAsync<PortfolioResponse>(cancellationToken);
        return portfolio!.Id;
    }
}
