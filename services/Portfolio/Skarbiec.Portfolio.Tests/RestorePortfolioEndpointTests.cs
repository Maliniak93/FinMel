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
public sealed class RestorePortfolioEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    /// <summary>spec-02 AC-9 (HTTP half — the outbox half is <c>PortfolioOutboxTests.RestorePortfolio_WritesPortfolioRestoredAndOnePositionEventPerAsset</c>).</summary>
    [Fact]
    public async Task Restore_ArchivedPortfolio_ReturnsOkAndClearsArchivedFlag()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        await client.PostAsync($"{PortfolioUri(portfolioId)}/archive", content: null, cancellationToken);

        var response = await client.PostAsync($"{PortfolioUri(portfolioId)}/restore", content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PortfolioResponse>(cancellationToken);
        Assert.False(body!.IsArchived);
    }

    /// <summary>
    /// spec-02 AC-10 (HTTP half — the "publishes nothing" half is
    /// <c>PortfolioOutboxTests.RestorePortfolio_NotArchivedPortfolio_WritesNoFurtherEvents</c>, since the
    /// 200 body is identical whether or not the no-op early return fires): restoring an already-active
    /// portfolio is a no-op (design decision 2).
    /// </summary>
    [Fact]
    public async Task Restore_NotArchivedPortfolio_ReturnsOkAndPublishesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);

        var response = await client.PostAsync($"{PortfolioUri(portfolioId)}/restore", content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PortfolioResponse>(cancellationToken);
        Assert.False(body!.IsArchived);
    }

    [Fact]
    public async Task Restore_NonExistentPortfolio_ReturnsNotFound()
    {
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PostAsync(
            $"{PortfolioUri(Guid.NewGuid())}/restore", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>spec-02 AC-11: a stranger's token gets 404, never 403 (leaks existence).</summary>
    [Fact]
    public async Task Restore_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var ownerClient = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await ownerClient.CreatePortfolioAsync(cancellationToken);
        await ownerClient.PostAsync($"{PortfolioUri(portfolioId)}/archive", content: null, cancellationToken);

        using var strangerClient = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await strangerClient.PostAsync($"{PortfolioUri(portfolioId)}/restore", content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
