using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Metals;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AssetGroupsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class AddMetalEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task GroupId_IsStored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var group = await client.CreateAssetGroupAsync(cancellationToken);

        var response = await client.PostAsJsonAsync(AllMetalsUri, NewMetalRequest() with { GroupId = group.Id }, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MetalResponse>(cancellationToken);
        Assert.Equal(group.Id, (await ReadAssetGroupStatesAsync(userId, cancellationToken, body!.AssetId))[body.AssetId].GroupId);
    }

    [Fact]
    public async Task AddMetal_NoHolding()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);

        var response = await client.PostAsJsonAsync(
            AllMetalsUri,
            NewMetalRequest(name: "Gold bar", metal: Metal.Gold, fineWeight: 50m, weightUnit: WeightUnit.Gram),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MetalResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(MetalUri(body.AssetId), response.Headers.Location?.OriginalString);
        Assert.Equal(50m, body.FineWeightGramsPerPiece);
        Assert.Equal(0m, body.Pieces);
        Assert.False(body.IsArchived);

        var asset = await client.GetAssetAsync(body.AssetId, cancellationToken);
        Assert.Equal(0m, asset.Quantity);
        Assert.Equal(0, asset.TransactionCount);
        Assert.Empty((await client.ListTransactionsAsync(body.AssetId, cancellationToken)).Items);

        await using var dbContext = CreateDbContext(userId);
        var holding = await dbContext.Set<MetalHolding>().SingleAsync(h => h.AssetId == body.AssetId, cancellationToken);
        Assert.Equal(userId, holding.UserId);
        Assert.Equal(Metal.Gold, holding.Metal);
        Assert.Equal(50m, holding.FineWeightGramsPerPiece);
    }

    [Theory]
    [InlineData("weight-zero")]
    [InlineData("weight-negative")]
    [InlineData("unknown-metal")]
    [InlineData("name-empty")]
    public async Task Add_Invalid_ReturnsBadRequest(string invalidCase)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var request = invalidCase switch
        {
            "weight-zero" => NewMetalRequest(fineWeight: 0m),
            "weight-negative" => NewMetalRequest(fineWeight: -1m),
            "unknown-metal" => NewMetalRequest(metal: (Metal)99),
            "name-empty" => NewMetalRequest(name: ""),
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase), invalidCase, null)
        };

        var response = await client.PostAsJsonAsync(AllMetalsUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var dbContext = CreateDbContext(userId);
        Assert.Equal(0, await dbContext.Assets.CountAsync(cancellationToken));
        Assert.Equal(0, await dbContext.Transactions.CountAsync(cancellationToken));
        Assert.Equal(0, await dbContext.Set<MetalHolding>().CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Add_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            AllMetalsUri, NewMetalRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
