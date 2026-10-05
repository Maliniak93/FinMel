using System.Net;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class GetBondEarlyRedemptionPreviewEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Preview_ValidRequest_ReturnsWhatEarlyRedemptionStoresAndWritesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(EdoEarlyRedemptionDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateEdoYearOneSettledAsync(cancellationToken);
        var portfolioId = funded.BondPortfolioId;
        var assetId = funded.Bond.AssetId;
        var rowsBefore = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.GetAsync(
            BondEarlyRedemptionPreviewUri(portfolioId, assetId, EdoEarlyRedemptionDate, 4, 4.00m), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.ReadJsonAsync(cancellationToken);
        Assert.Equal(2, preview.GetProperty("periodIndex").GetInt32());
        Assert.Equal(4, preview.GetProperty("bondCount").GetInt32());
        Assert.Equal(24.76m, preview.GetProperty("interestDue").GetDecimal());
        Assert.Equal(12.00m, preview.GetProperty("fee").GetDecimal());
        Assert.Equal(0m, preview.GetProperty("discountIncome").GetDecimal());
        Assert.Equal(12.76m, preview.GetProperty("taxableIncome").GetDecimal());
        Assert.Equal(2.43m, preview.GetProperty("tax").GetDecimal());
        Assert.Equal(410.33m, preview.GetProperty("proceeds").GetDecimal());
        Assert.Equal(rowsBefore, await SnapshotUserRowsAsync(userId, cancellationToken));
        Assert.Empty(await ReadBondRedemptionsAsync(userId, cancellationToken, assetId));

        await client.RedeemBondEarlyAsync(
            portfolioId, assetId, EdoEarlyRedemptionDate, 4, funded.CashAssetId, cancellationToken, runningPeriodRatePercent: 4.00m);

        var row = Assert.Single(await ReadBondRedemptionsAsync(userId, cancellationToken, assetId));
        Assert.Equal(preview.GetProperty("bondCount").GetInt32(), row.BondCount);
        Assert.Equal(preview.GetProperty("interestDue").GetDecimal(), row.AccruedInterest);
        Assert.Equal(preview.GetProperty("fee").GetDecimal(), row.Fee);
        Assert.Equal(preview.GetProperty("discountIncome").GetDecimal(), row.DiscountIncome);
        Assert.Equal(preview.GetProperty("tax").GetDecimal(), row.Tax);
        Assert.Equal(preview.GetProperty("proceeds").GetDecimal(), row.Proceeds);
    }
}
