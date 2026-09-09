using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class ListPortfoliosEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task List_ByDefault_ExcludesArchivedPortfolios()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var activeId = await client.CreatePortfolioAsync(cancellationToken, name: "Active");
        var archivedId = await client.CreatePortfolioAsync(cancellationToken, name: "Archived");
        await client.PostAsync($"{PortfolioUri(archivedId)}/archive", content: null, cancellationToken);

        var response = await client.GetAsync(PortfoliosUri, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<List<PortfolioResponse>>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(body!);
        Assert.Equal(activeId, body![0].Id);
    }

    [Fact]
    public async Task List_WithIncludeArchivedTrue_IncludesArchivedPortfolios()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        await client.CreatePortfolioAsync(cancellationToken, name: "Active");
        var archivedId = await client.CreatePortfolioAsync(cancellationToken, name: "Archived");
        await client.PostAsync($"{PortfolioUri(archivedId)}/archive", content: null, cancellationToken);

        var response = await client.GetAsync($"{PortfoliosUri}?includeArchived=true", cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<List<PortfolioResponse>>(cancellationToken);

        Assert.Equal(2, body!.Count);
    }

    /// <summary>M1.3: an out-of-set currency row must not break the list either — reads keep working.</summary>
    [Fact]
    public async Task List_IncludesPortfolioWithOutOfSetCurrency()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();

        await using (var seedContext = CreateDbContext(ownerId))
        {
            seedContext.Portfolios.Add(new PortfolioEntity { Id = portfolioId, Name = "Legacy GBP account", Currency = "GBP" });
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        using var client = Factory.CreateAuthenticatedClient(ownerId);
        var response = await client.GetAsync(PortfoliosUri, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<List<PortfolioResponse>>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var portfolio = Assert.Single(body!);
        Assert.Equal("GBP", portfolio.Currency);
    }
}
