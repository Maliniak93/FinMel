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
public sealed class GetPortfolioEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Get_ExistingPortfolio_ReturnsIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);

        var response = await client.GetAsync(PortfolioUri(portfolioId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PortfolioResponse>(cancellationToken);
        Assert.Equal(portfolioId, body!.Id);
    }

    [Fact]
    public async Task Get_NonExistentPortfolio_ReturnsNotFound()
    {
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync(PortfolioUri(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// M1.3: the currency restriction applies to writes only (T1.11's live session already created a
    /// USD portfolio before USD was even in the set) — a row holding an out-of-set currency must keep
    /// reading back unchanged, never rewritten and never a validation error, since GET has no request
    /// body for [SupportedCurrency] to inspect.
    /// </summary>
    [Fact]
    public async Task Get_PortfolioWithOutOfSetCurrency_ReadsBackUnchanged()
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
        var response = await client.GetAsync(PortfolioUri(portfolioId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PortfolioResponse>(cancellationToken);
        Assert.Equal("GBP", body!.Currency);
    }
}
