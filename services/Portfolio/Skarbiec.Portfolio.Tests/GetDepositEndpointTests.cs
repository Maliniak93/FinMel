using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

/// <summary>term-deposits: <c>GET .../deposits/{assetId}</c> returns the stored terms plus the read-time projection and status.</summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class GetDepositEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Get_ExistingDeposit_ReturnsTermsProjectionAndStatus()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Warsaw 2026-04-15 12:00 — two weeks past this deposit's 2026-04-01 maturity, so it is Due.
        Factory.Clock.SetUtcNow(new DateTimeOffset(2026, 4, 15, 10, 0, 0, TimeSpan.Zero));
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(
            cancellationToken, NewDepositRequest(capitalization: DepositCapitalization.Monthly, principal: 12_000m, startDate: new DateOnly(2026, 1, 1)));

        var response = await client.GetAsync(DepositUri(portfolioId, deposit.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DepositResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(deposit.AssetId, body.AssetId);
        Assert.Equal(portfolioId, body.PortfolioId);
        Assert.Equal("Savings", body.PortfolioName);
        Assert.Equal(12_000m, body.Principal);
        Assert.Equal(DepositCapitalization.Monthly, body.Capitalization);
        Assert.Equal(new DateOnly(2026, 4, 1), body.MaturityDate);
        Assert.Equal(178.24m, body.Projection.GrossInterest);
        Assert.Equal(33.87m, body.Projection.Tax);
        Assert.Equal(144.37m, body.Projection.NetInterest);
        Assert.Equal(12_144.37m, body.Projection.FinalAmount);
        Assert.Equal(DepositStatus.Due, body.Status);
    }

    /// <summary>A non-Deposit asset is not a deposit — 404, not its asset data.</summary>
    [Fact]
    public async Task Get_NonDepositAsset_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        var cashId = await client.AddCashAssetAsync(portfolioId, cancellationToken);

        var response = await client.GetAsync(DepositUri(portfolioId, cashId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        // Control: the deposit beside it is served by the same route.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(DepositUri(portfolioId, deposit.AssetId), cancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Get_DepositUnderWrongPortfolio_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (_, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        var otherPortfolioId = await client.CreatePortfolioAsync(cancellationToken, name: "Other portfolio");

        var response = await client.GetAsync(DepositUri(otherPortfolioId, deposit.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
