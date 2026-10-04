using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class PreviewBondInterestEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Preview_DueCouponPeriods_ReturnsTheRowsSettlingWouldStoreAndStoresNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateFundedBondAsync(cancellationToken, request: NewRorBondRequest());
        var assetId = funded.Bond.AssetId;

        var response = await client.PostAsJsonAsync(
            BondInterestPreviewUri(funded.BondPortfolioId, assetId),
            NewSettleBondBody([(1, null), (2, 3.75m), (3, 3.75m)], funded.CashAssetId),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.ReadJsonAsync(cancellationToken);
        var rows = preview.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(3, rows.Count);
        Assert.Equal(1, rows[0].GetProperty("periodIndex").GetInt32());
        Assert.Equal("2026-06-10", rows[0].GetProperty("start").GetString());
        Assert.Equal("2026-07-10", rows[0].GetProperty("end").GetString());
        Assert.Equal(4.00m, rows[0].GetProperty("ratePercent").GetDecimal());
        Assert.Equal(50, rows[0].GetProperty("bondCount").GetInt32());
        Assert.Equal(16.50m, rows[0].GetProperty("gross").GetDecimal());
        Assert.Equal(3.14m, rows[0].GetProperty("tax").GetDecimal());
        Assert.Equal(13.36m, rows[0].GetProperty("net").GetDecimal());
        Assert.Equal(3.75m, rows[2].GetProperty("ratePercent").GetDecimal());
        Assert.Equal(15.50m, rows[2].GetProperty("gross").GetDecimal());
        var totals = preview.GetProperty("totals");
        Assert.Equal(47.50m, totals.GetProperty("gross").GetDecimal());
        Assert.Equal(9.04m, totals.GetProperty("tax").GetDecimal());
        Assert.Equal(38.46m, totals.GetProperty("net").GetDecimal());

        Assert.Equal(0, await CountBondSettlementsAsync(userId, cancellationToken, assetId));
        Assert.Equal(1, (await client.ListTransactionsAsync(funded.BondPortfolioId, assetId, cancellationToken)).TotalCount);
        Assert.Equal(1_000m, (await client.GetAssetAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken)).Quantity);
        Assert.Equal(2, (await client.ListTransactionsAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken)).TotalCount);
    }
}
