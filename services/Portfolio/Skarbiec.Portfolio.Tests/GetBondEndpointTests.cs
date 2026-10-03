using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Bonds;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class GetBondEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Get_RorWithEndedPeriods_ReturnsDuePeriodsAndInterestDue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var request = NewBondRequest(
            name: "ROR0627",
            seriesCode: "ROR0627",
            type: TreasuryBondType.Ror,
            purchaseDate: new DateOnly(2026, 6, 10),
            marginPercent: null);
        var (portfolioId, bond) = await client.CreatePortfolioWithBondAsync(cancellationToken, request);

        var response = await client.GetAsync(BondUri(portfolioId, bond.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BondResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(new DateOnly(2027, 6, 10), body.MaturityDate);
        Assert.Equal(BondStatus.InterestDue, body.Status);
        Assert.Equal(12, body.Periods.Count);
        Assert.Equal(new DateOnly(2026, 7, 10), body.Periods[0].End);
        Assert.All(body.Periods.Take(3), p => Assert.Equal(BondPeriodState.Due, p.State));
        Assert.All(body.Periods.Skip(3), p => Assert.Equal(BondPeriodState.Upcoming, p.State));
    }

    [Fact]
    public async Task Get_PastMaturity_ReturnsMatured()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var request = NewBondRequest(
            name: "OTS0926",
            seriesCode: "OTS0926",
            type: TreasuryBondType.Ots,
            purchaseDate: new DateOnly(2026, 6, 10),
            marginPercent: null);
        var (portfolioId, bond) = await client.CreatePortfolioWithBondAsync(cancellationToken, request);

        var body = await client.GetBondAsync(portfolioId, bond.AssetId, cancellationToken);

        Assert.Equal(new DateOnly(2026, 9, 10), body.MaturityDate);
        Assert.Equal(BondStatus.Matured, body.Status);
    }

    [Fact]
    public async Task Get_NonBondAsset_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, bond) = await client.CreatePortfolioWithBondAsync(cancellationToken);
        var cashId = await client.AddCashAssetAsync(portfolioId, cancellationToken);

        var response = await client.GetAsync(BondUri(portfolioId, cashId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(BondUri(portfolioId, bond.AssetId), cancellationToken)).StatusCode);
    }
}
