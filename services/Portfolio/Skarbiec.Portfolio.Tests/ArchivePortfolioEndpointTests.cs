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
public sealed class ArchivePortfolioEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Archive_ExistingPortfolio_SetsIsArchivedTrue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);

        var response = await client.PostAsync($"{PortfolioUri(portfolioId)}/archive", content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PortfolioResponse>(cancellationToken);
        Assert.True(body!.IsArchived);
    }

    [Fact]
    public async Task Archive_NonExistentPortfolio_ReturnsNotFound()
    {
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PostAsync(
            $"{PortfolioUri(Guid.NewGuid())}/archive", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Archive_AlreadyArchivedPortfolio_IsIdempotent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        await client.PostAsync($"{PortfolioUri(portfolioId)}/archive", content: null, cancellationToken);

        var response = await client.PostAsync($"{PortfolioUri(portfolioId)}/archive", content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
