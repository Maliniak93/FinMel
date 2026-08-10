using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.GetPositionsForValuation;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

/// <summary>HTTP-level behavior of Features/GetPositionsForValuation (T2.11) — the endpoint Reporting's DailyPricesSynced consumer calls internally, across every user.</summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class GetPositionsForValuationEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    private const string PositionsForValuationUri = "/api/portfolio/positions-for-valuation";

    [Fact]
    public async Task Get_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync(PositionsForValuationUri, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_WithOrdinaryUserToken_ReturnsForbidden()
    {
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync(PositionsForValuationUri, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_WithSystemToken_ReturnsPositionsAcrossEveryUser()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var userAClient = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        using var userBClient = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var portfolioA = await userAClient.CreatePortfolioAsync(cancellationToken, name: "User A portfolio");
        var assetA = await userAClient.AddAssetAsync(portfolioA, cancellationToken, name: "Gold", assetClass: AssetClass.PreciousMetal, manualValue: 500m);

        var portfolioB = await userBClient.CreatePortfolioAsync(cancellationToken, name: "User B portfolio");
        var assetB = await userBClient.AddAssetAsync(portfolioB, cancellationToken, name: "Cash", assetClass: AssetClass.Cash, manualValue: 100m);

        using var systemClient = Factory.CreateSystemAuthenticatedClient();
        var response = await systemClient.GetAsync(PositionsForValuationUri, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<PositionsForValuationPage>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Contains(body.Items, p => p.AssetId == assetA && p.PortfolioId == portfolioA && p.ManualValueAmount == 500m);
        Assert.Contains(body.Items, p => p.AssetId == assetB && p.PortfolioId == portfolioB && p.ManualValueAmount == 100m);
    }

    [Fact]
    public async Task Get_ExcludesAssetsUnderArchivedPortfolios()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var activeId = await client.CreatePortfolioAsync(cancellationToken, name: "Active");
        var activeAssetId = await client.AddAssetAsync(activeId, cancellationToken, manualValue: 100m);

        var archivedId = await client.CreatePortfolioAsync(cancellationToken, name: "Archived");
        var archivedAssetId = await client.AddAssetAsync(archivedId, cancellationToken, manualValue: 900m);
        await client.PostAsync($"{PortfolioUri(archivedId)}/archive", content: null, cancellationToken);

        using var systemClient = Factory.CreateSystemAuthenticatedClient();
        var response = await systemClient.GetAsync(PositionsForValuationUri, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<PositionsForValuationPage>(cancellationToken);

        Assert.Contains(body!.Items, p => p.AssetId == activeAssetId);
        Assert.DoesNotContain(body.Items, p => p.AssetId == archivedAssetId);
    }

    [Fact]
    public async Task Get_PagesResultsAndReportsHasMore()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);

        for (var i = 0; i < 3; i++)
        {
            await client.AddAssetAsync(portfolioId, cancellationToken, name: $"Asset {i}", manualValue: 10m);
        }

        using var systemClient = Factory.CreateSystemAuthenticatedClient();
        var firstPageResponse = await systemClient.GetAsync($"{PositionsForValuationUri}?page=1&pageSize=2", cancellationToken);
        var firstPage = await firstPageResponse.Content.ReadFromJsonAsync<PositionsForValuationPage>(cancellationToken);

        Assert.Equal(2, firstPage!.Items.Count);
        Assert.True(firstPage.HasMore);

        var secondPageResponse = await systemClient.GetAsync($"{PositionsForValuationUri}?page=2&pageSize=2", cancellationToken);
        var secondPage = await secondPageResponse.Content.ReadFromJsonAsync<PositionsForValuationPage>(cancellationToken);

        Assert.Single(secondPage!.Items);
        Assert.False(secondPage.HasMore);
    }
}
