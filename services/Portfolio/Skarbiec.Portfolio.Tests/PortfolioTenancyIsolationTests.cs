using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.UpdatePortfolio;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using Skarbiec.Testing.Tenancy;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class PortfolioTenancyIsolationTests(SkarbiecContainersFixture containers) : TenancyIsolationTests<Program>
{
    protected override PortfolioApiFactory Factory { get; } = new(containers);

    protected override async Task<Uri> CreateResourceAsync(HttpClient ownerClient, CancellationToken cancellationToken)
    {
        var portfolioId = await ownerClient.CreatePortfolioAsync(cancellationToken);

        return new Uri(PortfolioUri(portfolioId), UriKind.Relative);
    }

    protected override Uri ListUrl { get; } = new(PortfoliosUri, UriKind.Relative);

    protected override HttpContent CreateUpdatePayload() =>
        JsonContent.Create(new UpdatePortfolioRequest { Name = "Stranger's edit", Currency = "PLN" });

    protected override async Task AssertResourceAbsentFromListAsync(HttpResponseMessage listResponse, CancellationToken cancellationToken)
    {
        var portfolios = await listResponse.Content.ReadFromJsonAsync<List<PortfolioResponse>>(cancellationToken);

        Assert.Empty(portfolios!);
    }

    [Fact]
    public async Task Delete_PortfolioWithAssetsByStranger_ReturnsNotFoundAndLeavesChildren()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetIds) = await owner.CreatePortfolioWithAssetsAndTransactionsAsync(
            cancellationToken, assetCount: 2, transactionsPerAsset: 2);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await stranger.DeleteAsync(PortfolioUri(portfolioId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var getPortfolio = await owner.GetAsync(PortfolioUri(portfolioId), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, getPortfolio.StatusCode);
        foreach (var assetId in assetIds)
        {
            var getAsset = await owner.GetAsync(AssetUri(portfolioId, assetId), cancellationToken);
            Assert.Equal(HttpStatusCode.OK, getAsset.StatusCode);
            Assert.Equal(2, (await owner.ListTransactionsAsync(portfolioId, assetId, cancellationToken)).TotalCount);
        }
    }
}
