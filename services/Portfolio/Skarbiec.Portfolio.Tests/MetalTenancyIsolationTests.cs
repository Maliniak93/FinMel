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
        await owner.AddMetalAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await stranger.AddMetalAsync(cancellationToken, NewMetalRequest(name: "Stranger's own"));

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
        var metal = await owner.AddMetalAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await stranger.GetAsync(MetalUri(metal.AssetId), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_ByStranger_ReturnsNotFoundAndLeavesHolding()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var metal = await owner.AddMetalAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var payload = NewUpdateMetalRequest(name: "Stranger's edit");

        var response = await stranger.PutAsJsonAsync(MetalUri(metal.AssetId), payload, cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var unchanged = await owner.GetMetalAsync(metal.AssetId, cancellationToken);
        Assert.Equal("Maple Leaf 1 oz", unchanged.Name);
        Assert.Equal(31.10347680m, unchanged.FineWeightGramsPerPiece);
    }

    [Fact]
    public async Task MetalHolding_QueriedAsStranger_IsFilteredOut()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var metal = await owner.AddMetalAsync(cancellationToken);

        await using var strangerDb = CreateDbContext(Guid.NewGuid());
        Assert.False(await strangerDb.Set<MetalHolding>().AnyAsync(h => h.AssetId == metal.AssetId, cancellationToken));
        await using var ownerDb = CreateDbContext(ownerId);
        Assert.True(await ownerDb.Set<MetalHolding>().AnyAsync(h => h.AssetId == metal.AssetId, cancellationToken));
    }
}
