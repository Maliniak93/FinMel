using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Metals;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class MetalTenancyIsolationTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task List_ByStranger_DoesNotIncludeOwnersHolding()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await owner.CreatePortfolioWithMetalAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await stranger.CreatePortfolioWithMetalAsync(cancellationToken, NewMetalRequest(name: "Stranger's own"));

        var response = await stranger.GetAsync(AllMetalsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var listed = Assert.Single((await response.Content.ReadFromJsonAsync<List<MetalResponse>>(cancellationToken))!);
        Assert.Equal("Stranger's own", listed.Name);
    }

    [Fact]
    public async Task Get_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (ownerPortfolioId, metal) = await owner.CreatePortfolioWithMetalAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerPortfolioId = await stranger.CreatePortfolioAsync(cancellationToken);

        var viaOwnersPortfolio = await stranger.GetAsync(MetalUri(ownerPortfolioId, metal.AssetId), cancellationToken);
        var viaStrangersPortfolio = await stranger.GetAsync(MetalUri(strangerPortfolioId, metal.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, viaOwnersPortfolio.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, viaStrangersPortfolio.StatusCode);
    }

    [Fact]
    public async Task Put_ByStranger_ReturnsNotFoundAndLeavesHolding()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (ownerPortfolioId, metal) = await owner.CreatePortfolioWithMetalAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerPortfolioId = await stranger.CreatePortfolioAsync(cancellationToken);
        var payload = NewUpdateMetalRequest(name: "Stranger's edit");

        var viaOwnersPortfolio = await stranger.PutAsJsonAsync(MetalUri(ownerPortfolioId, metal.AssetId), payload, cancellationToken);
        var viaStrangersPortfolio = await stranger.PutAsJsonAsync(MetalUri(strangerPortfolioId, metal.AssetId), payload, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, viaOwnersPortfolio.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, viaStrangersPortfolio.StatusCode);
        var unchanged = await owner.GetMetalAsync(ownerPortfolioId, metal.AssetId, cancellationToken);
        Assert.Equal("Maple Leaf 1 oz", unchanged.Name);
        Assert.Equal(31.10347680m, unchanged.FineWeightGramsPerPiece);
    }

    [Fact]
    public async Task Add_IntoStrangersPortfolio_ReturnsNotFoundAndWritesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var ownerPortfolioId = await owner.CreatePortfolioAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await stranger.PostAsJsonAsync(MetalsUri(ownerPortfolioId), NewMetalRequest(), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var dbContext = CreateDbContext(ownerId);
        Assert.Equal(0, await dbContext.Set<MetalHolding>().IgnoreQueryFilters().CountAsync(cancellationToken));
        Assert.Equal(0, await dbContext.Assets.IgnoreQueryFilters().CountAsync(cancellationToken));
        Assert.Equal(0, await dbContext.Transactions.IgnoreQueryFilters().CountAsync(cancellationToken));
    }

    [Fact]
    public async Task MetalHolding_QueriedAsStranger_IsFilteredOut()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var (_, metal) = await owner.CreatePortfolioWithMetalAsync(cancellationToken);

        await using var strangerDb = CreateDbContext(Guid.NewGuid());
        Assert.False(await strangerDb.Set<MetalHolding>().AnyAsync(h => h.AssetId == metal.AssetId, cancellationToken));
        await using var ownerDb = CreateDbContext(ownerId);
        Assert.True(await ownerDb.Set<MetalHolding>().AnyAsync(h => h.AssetId == metal.AssetId, cancellationToken));
    }
}
