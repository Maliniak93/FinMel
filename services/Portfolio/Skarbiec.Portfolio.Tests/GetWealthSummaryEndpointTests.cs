using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.GetWealthSummary;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class GetWealthSummaryEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Get_SumsManualValuesAcrossPortfolios_IntoTotalAndBreakdowns()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var retirementId = await client.CreatePortfolioAsync(cancellationToken, name: "Retirement");
        await client.AddAssetAsync(retirementId, cancellationToken, name: "Apple", assetClass: AssetClass.Stock, manualValue: 700m);
        await client.AddAssetAsync(retirementId, cancellationToken, name: "Bank account", assetClass: AssetClass.Cash, manualValue: 300m);

        var savingsId = await client.CreatePortfolioAsync(cancellationToken, name: "Savings");
        await client.AddAssetAsync(savingsId, cancellationToken, name: "Gold", assetClass: AssetClass.PreciousMetal, manualValue: 1000m);

        var response = await client.GetAsync(WealthSummaryUri, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<WealthSummaryResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2000m, body!.TotalNetWorth);
        Assert.Equal("PLN", body.BaseCurrency);
        Assert.True(body.FxConversionIsNaive);

        Assert.Equal(3, body.ByAssetClass.Count);
        var stock = Assert.Single(body.ByAssetClass, c => c.AssetClass == AssetClass.Stock);
        Assert.Equal(700m, stock.Value);
        Assert.Equal(35m, stock.Percentage);
        var gold = Assert.Single(body.ByAssetClass, c => c.AssetClass == AssetClass.PreciousMetal);
        Assert.Equal(1000m, gold.Value);
        Assert.Equal(50m, gold.Percentage);

        Assert.Equal(2, body.ByPortfolio.Count);
        Assert.Equal(1000m, Assert.Single(body.ByPortfolio, p => p.PortfolioId == retirementId).Value);
        Assert.Equal(1000m, Assert.Single(body.ByPortfolio, p => p.PortfolioId == savingsId).Value);
    }

    [Fact]
    public async Task Get_ExcludesArchivedPortfolios_FromTotalAndBreakdowns()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var activeId = await client.CreatePortfolioAsync(cancellationToken, name: "Active");
        await client.AddAssetAsync(activeId, cancellationToken, manualValue: 100m);

        var archivedId = await client.CreatePortfolioAsync(cancellationToken, name: "Archived");
        await client.AddAssetAsync(archivedId, cancellationToken, manualValue: 900m);
        await client.PostAsync($"{PortfolioUri(archivedId)}/archive", content: null, cancellationToken);

        var response = await client.GetAsync(WealthSummaryUri, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<WealthSummaryResponse>(cancellationToken);

        Assert.Equal(100m, body!.TotalNetWorth);
        Assert.Equal(activeId, Assert.Single(body.ByPortfolio).PortfolioId);
    }

    [Fact]
    public async Task Get_WithNoPortfolios_ReturnsZeroTotalAndEmptyBreakdowns()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync(WealthSummaryUri, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<WealthSummaryResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0m, body!.TotalNetWorth);
        Assert.Empty(body.ByAssetClass);
        Assert.Empty(body.ByPortfolio);
    }

    [Fact]
    public async Task Get_NeverIncludesAnotherUsersAssets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var userAClient = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        using var userBClient = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var userAPortfolioId = await userAClient.CreatePortfolioAsync(cancellationToken, name: "User A portfolio");
        await userAClient.AddAssetAsync(userAPortfolioId, cancellationToken, manualValue: 5000m);

        var response = await userBClient.GetAsync(WealthSummaryUri, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<WealthSummaryResponse>(cancellationToken);

        Assert.Equal(0m, body!.TotalNetWorth);
        Assert.Empty(body.ByPortfolio);
    }
}
