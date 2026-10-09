using System.Net;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class GetBondRedemptionPreviewEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Preview_MaturedSettled_ReturnsWhatRedeemStoresAndWritesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterTosMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateSettledTosAsync(cancellationToken);
        var portfolioId = funded.BondPortfolioId;
        var assetId = funded.Bond.AssetId;
        var rowsBefore = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.GetAsync(BondRedemptionPreviewUri(portfolioId, assetId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.ReadJsonAsync(cancellationToken);
        Assert.Equal(TosMaturityDate, DateOnly.Parse(preview.GetProperty("date").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(10, preview.GetProperty("bondCount").GetInt32());
        Assert.Equal(137.90m, preview.GetProperty("capitalisedInterest").GetDecimal());
        Assert.Equal(0m, preview.GetProperty("discountIncome").GetDecimal());
        Assert.Equal(137.90m, preview.GetProperty("taxableIncome").GetDecimal());
        Assert.Equal(26.21m, preview.GetProperty("tax").GetDecimal());
        Assert.Equal(1_111.69m, preview.GetProperty("proceeds").GetDecimal());
        Assert.Equal(rowsBefore, await SnapshotUserRowsAsync(userId, cancellationToken));
        Assert.Empty(await ReadBondRedemptionsAsync(userId, cancellationToken));

        await client.RedeemBondAsync(portfolioId, assetId, funded.CashAssetId, cancellationToken);

        var row = Assert.Single(await ReadBondRedemptionsAsync(userId, cancellationToken, assetId));
        Assert.Equal(preview.GetProperty("bondCount").GetInt32(), row.BondCount);
        Assert.Equal(preview.GetProperty("capitalisedInterest").GetDecimal(), row.CapitalisedInterest);
        Assert.Equal(preview.GetProperty("discountIncome").GetDecimal(), row.DiscountIncome);
        Assert.Equal(preview.GetProperty("tax").GetDecimal(), row.Tax);
        Assert.Equal(preview.GetProperty("proceeds").GetDecimal(), row.Proceeds);
        Assert.Equal(TosMaturityDate, row.Date);
    }

    [Fact]
    public async Task Preview_BeforeMaturity_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var funded = await client.CreateFundedBondAsync(cancellationToken, request: NewTosBondRequest());

        var response = await client.GetAsync(BondRedemptionPreviewUri(funded.BondPortfolioId, funded.Bond.AssetId), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.BondNotMaturedErrorCode, cancellationToken);
    }
}
