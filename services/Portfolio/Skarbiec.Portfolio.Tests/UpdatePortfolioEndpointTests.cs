using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.CreatePortfolio;
using Skarbiec.Portfolio.Features.UpdatePortfolio;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class UpdatePortfolioEndpointTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private const string PortfoliosUri = "/api/portfolio/portfolios";

    private readonly PortfolioApiFactory _factory = new(containers);

    public ValueTask InitializeAsync() => new(_factory.ResetDatabaseAsync());

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Update_WithValidRequest_ChangesFields()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var created = await client.PostAsJsonAsync(
            PortfoliosUri, new CreatePortfolioRequest { Name = "Retirement", Currency = "PLN" }, cancellationToken);
        var portfolio = await created.Content.ReadFromJsonAsync<PortfolioResponse>(cancellationToken);

        var response = await client.PutAsJsonAsync(
            $"{PortfoliosUri}/{portfolio!.Id}",
            new UpdatePortfolioRequest { Name = "Retirement (renamed)", Description = "Updated", Currency = "USD" },
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PortfolioResponse>(cancellationToken);
        Assert.Equal("Retirement (renamed)", body!.Name);
        Assert.Equal("USD", body.Currency);
    }

    [Fact]
    public async Task Update_ToNameTakenByAnotherOwnPortfolio_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.PostAsJsonAsync(PortfoliosUri, new CreatePortfolioRequest { Name = "Taken", Currency = "PLN" }, cancellationToken);
        var created = await client.PostAsJsonAsync(PortfoliosUri, new CreatePortfolioRequest { Name = "Free", Currency = "PLN" }, cancellationToken);
        var portfolio = await created.Content.ReadFromJsonAsync<PortfolioResponse>(cancellationToken);

        var response = await client.PutAsJsonAsync(
            $"{PortfoliosUri}/{portfolio!.Id}", new UpdatePortfolioRequest { Name = "Taken", Currency = "PLN" }, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_NonExistentPortfolio_ReturnsNotFound()
    {
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PutAsJsonAsync(
            $"{PortfoliosUri}/{Guid.NewGuid()}",
            new UpdatePortfolioRequest { Name = "Anything", Currency = "PLN" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
