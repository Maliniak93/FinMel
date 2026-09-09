using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class ListAssetsEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task List_ReturnsOnlyAssetsOfThatPortfolio()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioA = await client.CreatePortfolioAsync(cancellationToken, name: "A");
        var portfolioB = await client.CreatePortfolioAsync(cancellationToken, name: "B");
        await client.AddAssetAsync(portfolioA, cancellationToken, name: "Asset in A");
        await client.AddAssetAsync(portfolioB, cancellationToken, name: "Asset in B");

        var response = await client.GetAsync(AssetsUri(portfolioA), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var assets = await response.Content.ReadFromJsonAsync<List<AssetResponse>>(cancellationToken);
        var asset = Assert.Single(assets!);
        Assert.Equal("Asset in A", asset.Name);
    }

    [Fact]
    public async Task List_PortfolioWithNoAssets_ReturnsEmptyList()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);

        var response = await client.GetAsync(AssetsUri(portfolioId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var assets = await response.Content.ReadFromJsonAsync<List<AssetResponse>>(cancellationToken);
        Assert.Empty(assets!);
    }

    [Fact]
    public async Task List_NonExistentPortfolio_ReturnsNotFound()
    {
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync(AssetsUri(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>M1.3: an out-of-set currency row must not break the list either — reads keep working.</summary>
    [Fact]
    public async Task List_IncludesAssetWithOutOfSetCurrency()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        var (portfolioId, assetId) = (Guid.NewGuid(), Guid.NewGuid());

        await using (var seedContext = CreateDbContext(ownerId))
        {
            seedContext.Portfolios.Add(new PortfolioEntity { Id = portfolioId, Name = "Legacy account", Currency = "PLN" });
            seedContext.Assets.Add(new Asset
            {
                Id = assetId,
                PortfolioId = portfolioId,
                AssetClass = AssetClass.Cash,
                ValuationMode = AssetValuationMode.Manual,
                Name = "Legacy GBP cash",
                Currency = "GBP",
                ManualValueAmount = 100m,
                ManualValueDate = new DateOnly(2026, 1, 1)
            });
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        using var client = Factory.CreateAuthenticatedClient(ownerId);
        var response = await client.GetAsync(AssetsUri(portfolioId), cancellationToken);
        var assets = await response.Content.ReadFromJsonAsync<List<AssetResponse>>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var asset = Assert.Single(assets!);
        Assert.Equal("GBP", asset.Currency);
    }
}
