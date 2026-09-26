using System.Net;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

/// <summary>
/// term-deposits-settlement: <c>GET .../deposits/{assetId}/settlement-preview</c> answers a Due
/// deposit with the part-1 <c>DepositInterestMath</c> projection and a settlement date defaulting to
/// the maturity date — the values the settle dialog is pre-filled with. Asserted on the wire JSON, so
/// the facts hold whatever the response record is called.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class SettlementPreviewEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    /// <summary>AC-1.</summary>
    [Fact]
    public async Task Preview_DueDeposit_ReturnsProjection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        // 10 000.00 at 6 % from 2026-01-15 for 3 months → matures 2026-04-15: gross 147.95, tax 28.12, net 119.83.
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);

        var response = await client.GetAsync(DepositSettlementPreviewUri(portfolioId, deposit.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.ReadJsonAsync(cancellationToken);
        Assert.Equal("2026-04-15", preview.GetProperty("settledOn").GetString());
        Assert.Equal(147.95m, preview.GetProperty("grossInterest").GetDecimal());
        Assert.Equal(28.12m, preview.GetProperty("tax").GetDecimal());
        Assert.Equal(119.83m, preview.GetProperty("netInterest").GetDecimal());
        Assert.Equal(10_119.83m, preview.GetProperty("finalAmount").GetDecimal());
    }

    /// <summary>AC-2 (preview half): the maturity date is tomorrow in Warsaw — not Due yet.</summary>
    [Fact]
    public async Task Preview_BeforeMaturity_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Warsaw 2026-04-14 12:00 — the day before the 2026-04-15 maturity.
        Factory.Clock.SetUtcNow(new DateTimeOffset(2026, 4, 14, 10, 0, 0, TimeSpan.Zero));
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);

        var response = await client.GetAsync(DepositSettlementPreviewUri(portfolioId, deposit.AssetId), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.DepositNotDueErrorCode, cancellationToken);
    }

    /// <summary>A settled deposit has nothing left to preview.</summary>
    [Fact]
    public async Task Preview_SettledDeposit_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        await client.SettleDepositAsync(portfolioId, deposit.AssetId, cancellationToken);

        var response = await client.GetAsync(DepositSettlementPreviewUri(portfolioId, deposit.AssetId), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.DepositAlreadySettledErrorCode, cancellationToken);
    }

    /// <summary>The deposit endpoints address Deposit-class assets only.</summary>
    [Fact]
    public async Task Preview_NonDepositAsset_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        var cashId = await client.AddCashAssetAsync(portfolioId, cancellationToken);

        var response = await client.GetAsync(DepositSettlementPreviewUri(portfolioId, cashId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        // Control: the Due deposit beside it is served by the same route.
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync(DepositSettlementPreviewUri(portfolioId, deposit.AssetId), cancellationToken)).StatusCode);
    }
}
