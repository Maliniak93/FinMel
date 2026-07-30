using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.CreatePortfolio;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class GetPortfolioEndpointTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private const string PortfoliosUri = "/api/portfolio/portfolios";

    private readonly PortfolioApiFactory _factory = new(containers);

    public ValueTask InitializeAsync() => new(_factory.ResetDatabaseAsync());

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Get_ExistingPortfolio_ReturnsIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var created = await client.PostAsJsonAsync(
            PortfoliosUri, new CreatePortfolioRequest { Name = "Retirement", Currency = "PLN" }, cancellationToken);
        var portfolio = await created.Content.ReadFromJsonAsync<PortfolioResponse>(cancellationToken);

        var response = await client.GetAsync($"{PortfoliosUri}/{portfolio!.Id}", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PortfolioResponse>(cancellationToken);
        Assert.Equal(portfolio.Id, body!.Id);
    }

    [Fact]
    public async Task Get_NonExistentPortfolio_ReturnsNotFound()
    {
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync($"{PortfoliosUri}/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
