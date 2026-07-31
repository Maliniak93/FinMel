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
public sealed class GetAssetEndpointTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private const string PortfoliosUri = "/api/portfolio/portfolios";

    private readonly PortfolioApiFactory _factory = new(containers);

    public ValueTask InitializeAsync() => new(_factory.ResetDatabaseAsync());

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Get_ExistingAsset_ReturnsIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioResponse = await client.PostAsJsonAsync(
            PortfoliosUri, new CreatePortfolioRequest { Name = "Retirement", Currency = "PLN" }, cancellationToken);
        var portfolio = await portfolioResponse.Content.ReadFromJsonAsync<PortfolioResponse>(cancellationToken);
        var addRequest = new AddAssetRequest
        {
            AssetClass = AssetClass.Stock,
            Name = "Shares",
            Currency = "USD",
            ManualValue = 250m,
            ManualValueDate = new DateOnly(2026, 3, 1)
        };
        var addResponse = await client.PostAsJsonAsync($"{PortfoliosUri}/{portfolio!.Id}/assets", addRequest, cancellationToken);
        var created = await addResponse.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);

        var response = await client.GetAsync($"{PortfoliosUri}/{portfolio.Id}/assets/{created!.Id}", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal(created.Id, body!.Id);
        Assert.Equal("Shares", body.Name);
    }

    [Fact]
    public async Task Get_NonExistentAsset_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioResponse = await client.PostAsJsonAsync(
            PortfoliosUri, new CreatePortfolioRequest { Name = "Retirement", Currency = "PLN" }, cancellationToken);
        var portfolio = await portfolioResponse.Content.ReadFromJsonAsync<PortfolioResponse>(cancellationToken);

        var response = await client.GetAsync($"{PortfoliosUri}/{portfolio!.Id}/assets/{Guid.NewGuid()}", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
