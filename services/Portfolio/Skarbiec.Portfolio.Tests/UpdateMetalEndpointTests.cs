using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class UpdateMetalEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Update_GoldToSilverWithGramWeight_MovesInstrumentAndKeepsPieces()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, metal) = await client.CreatePortfolioWithMetalAsync(
            cancellationToken, NewMetalRequest(name: "Krugerrand", metal: Metal.Gold, pieces: 2m));

        var response = await client.PutAsJsonAsync(
            MetalUri(portfolioId, metal.AssetId), NewUpdateMetalRequest(name: "Silver bar"), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await client.GetMetalAsync(portfolioId, metal.AssetId, cancellationToken);
        Assert.Equal("Silver bar", updated.Name);
        Assert.Equal(Metal.Silver, updated.Metal);
        Assert.Equal(100m, updated.FineWeightGramsPerPiece);
        Assert.Equal(2m, updated.Pieces);
        Assert.Equal(200m, updated.TotalFineGrams);
        await using var dbContext = CreateDbContext(userId);
        var asset = await dbContext.Assets.SingleAsync(a => a.Id == metal.AssetId, cancellationToken);
        Assert.Equal(MetalInstruments.InstrumentIdFor(Metal.Silver), asset.InstrumentId);
    }

    [Fact]
    public async Task Update_ArchivedPortfolio_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, metal) = await client.CreatePortfolioWithMetalAsync(cancellationToken);
        await client.ArchivePortfolioAsync(portfolioId, cancellationToken);

        var response = await client.PutAsJsonAsync(
            MetalUri(portfolioId, metal.AssetId), NewUpdateMetalRequest(), cancellationToken);

        await response.AssertPortfolioArchivedConflictAsync(cancellationToken);
        var unchanged = await client.GetMetalAsync(portfolioId, metal.AssetId, cancellationToken);
        Assert.Equal("Maple Leaf 1 oz", unchanged.Name);
        Assert.Equal(31.10347680m, unchanged.FineWeightGramsPerPiece);
    }

    [Fact]
    public async Task Update_ArchivedHolding_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, metal) = await client.CreatePortfolioWithMetalAsync(cancellationToken);
        await client.ArchiveAssetAsync(portfolioId, metal.AssetId, cancellationToken);

        var response = await client.PutAsJsonAsync(
            MetalUri(portfolioId, metal.AssetId), NewUpdateMetalRequest(), cancellationToken);

        await response.AssertAssetArchivedConflictAsync(cancellationToken);
        var unchanged = await client.GetMetalAsync(portfolioId, metal.AssetId, cancellationToken);
        Assert.Equal("Maple Leaf 1 oz", unchanged.Name);
        Assert.Equal(Metal.Silver, unchanged.Metal);
    }
}
