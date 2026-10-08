using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Metals;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class AddMetalEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Add_WithFirstPurchase_CreatesHoldingAndBuy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken, name: "Metals");

        var response = await client.PostAsJsonAsync(MetalsUri(portfolioId), NewMetalRequest(), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MetalResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(MetalUri(portfolioId, body.AssetId), response.Headers.Location?.OriginalString);
        Assert.Equal(portfolioId, body.PortfolioId);
        Assert.Equal("Metals", body.PortfolioName);
        Assert.Equal("Maple Leaf 1 oz", body.Name);
        Assert.Equal(Metal.Silver, body.Metal);
        Assert.Equal(31.10347680m, body.FineWeightGramsPerPiece);
        Assert.Equal(10m, body.Pieces);
        Assert.Equal(311.0347680m, body.TotalFineGrams);
        Assert.False(body.IsArchived);

        var asset = await client.GetAssetAsync(portfolioId, body.AssetId, cancellationToken);
        Assert.Equal(AssetClass.PreciousMetal, asset.AssetClass);
        Assert.Equal(AssetValuationMode.Market, asset.ValuationMode);
        Assert.Equal("PLN", asset.Currency);
        Assert.Equal(10m, asset.Quantity);
        Assert.Equal(31.10347680m, asset.FineWeightGramsPerPiece);
        Assert.Equal(1, asset.TransactionCount);

        var buy = Assert.Single((await client.ListTransactionsAsync(portfolioId, body.AssetId, cancellationToken)).Items);
        Assert.Equal(TransactionType.Buy, buy.Type);
        Assert.Equal(10m, buy.Quantity);
        Assert.Equal(260.00m, buy.UnitPrice);
        Assert.Equal(MetalPurchaseDate, buy.Date);
        Assert.Equal(2_600.00m, buy.ValuePln);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(portfolioId, body.AssetId, cancellationToken);

        await using var dbContext = CreateDbContext(userId);
        var storedAsset = await dbContext.Assets.SingleAsync(a => a.Id == body.AssetId, cancellationToken);
        Assert.Equal(MetalInstruments.InstrumentIdFor(Metal.Silver), storedAsset.InstrumentId);
        var holding = await dbContext.Set<MetalHolding>().SingleAsync(h => h.AssetId == body.AssetId, cancellationToken);
        Assert.Equal(userId, holding.UserId);
        Assert.Equal(Metal.Silver, holding.Metal);
        Assert.Equal(31.10347680m, holding.FineWeightGramsPerPiece);
    }

    [Fact]
    public async Task Add_WithoutFirstPurchase_StartsEmptyWithGramWeight()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);

        var response = await client.PostAsJsonAsync(
            MetalsUri(portfolioId),
            NewMetalRequest(name: "Gold bar", metal: Metal.Gold, fineWeight: 50m, weightUnit: WeightUnit.Gram, withFirstPurchase: false),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MetalResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(50m, body.FineWeightGramsPerPiece);
        Assert.Equal(0m, body.Pieces);
        Assert.Empty((await client.ListTransactionsAsync(portfolioId, body.AssetId, cancellationToken)).Items);
    }

    [Theory]
    [InlineData("weight-zero")]
    [InlineData("weight-negative")]
    [InlineData("unknown-metal")]
    [InlineData("name-empty")]
    [InlineData("pieces-zero")]
    [InlineData("date-tomorrow")]
    [InlineData("price-negative")]
    public async Task Add_Invalid_ReturnsBadRequest(string invalidCase)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var request = invalidCase switch
        {
            "weight-zero" => NewMetalRequest(fineWeight: 0m),
            "weight-negative" => NewMetalRequest(fineWeight: -1m),
            "unknown-metal" => NewMetalRequest(metal: (Metal)99),
            "name-empty" => NewMetalRequest(name: ""),
            "pieces-zero" => NewMetalRequest(pieces: 0m),
            "date-tomorrow" => NewMetalRequest(purchaseDate: DateOnly.FromDateTime(MetalTodayUtc.UtcDateTime).AddDays(1)),
            "price-negative" => NewMetalRequest(pricePerPiece: -1m),
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase), invalidCase, null)
        };

        var response = await client.PostAsJsonAsync(MetalsUri(portfolioId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var dbContext = CreateDbContext(userId);
        Assert.Equal(0, await dbContext.Assets.CountAsync(cancellationToken));
        Assert.Equal(0, await dbContext.Transactions.CountAsync(cancellationToken));
        Assert.Equal(0, await dbContext.Set<MetalHolding>().CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Add_ArchivedPortfolio_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        await client.ArchivePortfolioAsync(portfolioId, cancellationToken);

        var response = await client.PostAsJsonAsync(MetalsUri(portfolioId), NewMetalRequest(), cancellationToken);

        await response.AssertPortfolioArchivedConflictAsync(cancellationToken);
        await using var dbContext = CreateDbContext(userId);
        Assert.Equal(0, await dbContext.Assets.CountAsync(cancellationToken));
        Assert.Equal(0, await dbContext.Set<MetalHolding>().CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Add_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            MetalsUri(Guid.NewGuid()), NewMetalRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
