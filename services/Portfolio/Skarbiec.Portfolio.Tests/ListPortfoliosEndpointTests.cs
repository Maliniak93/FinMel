using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.CreatePortfolio;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class ListPortfoliosEndpointTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private const string PortfoliosUri = "/api/portfolio/portfolios";

    private readonly PortfolioApiFactory _factory = new(containers);

    public ValueTask InitializeAsync() => new(_factory.ResetDatabaseAsync());

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task List_ByDefault_ExcludesArchivedPortfolios()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());

        var active = await CreateAsync(client, "Active", cancellationToken);
        var archived = await CreateAsync(client, "Archived", cancellationToken);
        await client.PostAsync($"{PortfoliosUri}/{archived.Id}/archive", content: null, cancellationToken);

        var response = await client.GetAsync(PortfoliosUri, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<List<PortfolioResponse>>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(body!);
        Assert.Equal(active.Id, body![0].Id);
    }

    [Fact]
    public async Task List_WithIncludeArchivedTrue_IncludesArchivedPortfolios()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());

        await CreateAsync(client, "Active", cancellationToken);
        var archived = await CreateAsync(client, "Archived", cancellationToken);
        await client.PostAsync($"{PortfoliosUri}/{archived.Id}/archive", content: null, cancellationToken);

        var response = await client.GetAsync($"{PortfoliosUri}?includeArchived=true", cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<List<PortfolioResponse>>(cancellationToken);

        Assert.Equal(2, body!.Count);
    }

    private static async Task<PortfolioResponse> CreateAsync(HttpClient client, string name, CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync(
            PortfoliosUri, new CreatePortfolioRequest { Name = name, Currency = "PLN" }, cancellationToken);

        return (await response.Content.ReadFromJsonAsync<PortfolioResponse>(cancellationToken))!;
    }
}
