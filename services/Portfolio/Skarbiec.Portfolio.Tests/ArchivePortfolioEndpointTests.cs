using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.CreatePortfolio;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class ArchivePortfolioEndpointTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private const string PortfoliosUri = "/api/portfolio/portfolios";

    private readonly PortfolioApiFactory _factory = new(containers);

    public ValueTask InitializeAsync() => new(_factory.ResetDatabaseAsync());

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Archive_ExistingPortfolio_SetsIsArchivedTrue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var created = await client.PostAsJsonAsync(
            PortfoliosUri, new CreatePortfolioRequest { Name = "Retirement", Currency = "PLN" }, cancellationToken);
        var portfolio = await created.Content.ReadFromJsonAsync<PortfolioResponse>(cancellationToken);

        var response = await client.PostAsync($"{PortfoliosUri}/{portfolio!.Id}/archive", content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PortfolioResponse>(cancellationToken);
        Assert.True(body!.IsArchived);
    }

    [Fact]
    public async Task Archive_NonExistentPortfolio_ReturnsNotFound()
    {
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PostAsync($"{PortfoliosUri}/{Guid.NewGuid()}/archive", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Archive_AlreadyArchivedPortfolio_IsIdempotent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var created = await client.PostAsJsonAsync(
            PortfoliosUri, new CreatePortfolioRequest { Name = "Retirement", Currency = "PLN" }, cancellationToken);
        var portfolio = await created.Content.ReadFromJsonAsync<PortfolioResponse>(cancellationToken);
        await client.PostAsync($"{PortfoliosUri}/{portfolio!.Id}/archive", content: null, cancellationToken);

        var response = await client.PostAsync($"{PortfoliosUri}/{portfolio.Id}/archive", content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
