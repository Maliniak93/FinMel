using System.Net;
using System.Net.Http.Json;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Features.GetDashboard;
using Skarbiec.Reporting.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Reporting.Tests.Fixtures.ReportingApi;

namespace Skarbiec.Reporting.Tests;

/// <summary>
/// spec-07 AC12, end to end through the real host: an <c>AssetPositionChanged</c> consumed by the
/// host's own running bus shows up on <c>GET /dashboard</c> with no <c>DailyPricesSynced</c> in
/// between. The dashboard is the endpoint under test, so it is called directly and asserted on the
/// raw response; <see cref="ReportingApi.GetDashboardWhenAsync"/> only waits for the asynchronous
/// consume to land before handing that response back.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class DashboardFreshnessTests(SkarbiecContainersFixture containers) : ReportingEndpointTests(containers)
{
    [Fact]
    public async Task AddCash_ThenGetDashboard_ShowsNewNetWorthWithoutSync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();

        using var client = Factory.CreateAuthenticatedClient(userId);
        await client.WaitUntilReadyAsync(cancellationToken);

        await PublishCashAsync(userId, portfolioId, 500m, cancellationToken);

        using var response = await client.GetDashboardWhenAsync(d => d.NetWorthPln == 500m, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<DashboardResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(500m, body!.NetWorthPln);
        Assert.False(body.IsStale);
        Assert.Equal(portfolioId, Assert.Single(body.ByPortfolio).PortfolioId);
        Assert.Equal(AssetClass.Cash, Assert.Single(body.ByAssetClass).AssetClass);
    }

    [Fact]
    public async Task AddCash_NeverAppearsOnAnotherUsersDashboard()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userAId = Guid.NewGuid();
        var userBId = Guid.NewGuid();

        using var userAClient = Factory.CreateAuthenticatedClient(userAId);
        await userAClient.WaitUntilReadyAsync(cancellationToken);

        await PublishCashAsync(userAId, Guid.NewGuid(), 500m, cancellationToken);

        // User A's own dashboard reaching 500 is the signal the event was consumed — asserting B
        // without it would pass vacuously on an event that simply hadn't landed yet.
        using var userAResponse = await userAClient.GetDashboardWhenAsync(d => d.NetWorthPln == 500m, cancellationToken);
        var userABody = await userAResponse.Content.ReadFromJsonAsync<DashboardResponse>(cancellationToken);
        Assert.Equal(500m, userABody!.NetWorthPln);

        using var userBClient = Factory.CreateAuthenticatedClient(userBId);
        using var userBResponse = await userBClient.GetAsync(DashboardUri, cancellationToken);
        var userBBody = await userBResponse.Content.ReadFromJsonAsync<DashboardResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, userBResponse.StatusCode);
        Assert.Equal(0m, userBBody!.NetWorthPln);
        Assert.Null(userBBody.AsOf);
        Assert.Empty(userBBody.ByPortfolio);
        Assert.Empty(userBBody.ByAssetClass);
    }

    /// <summary>What Portfolio's <c>AddAsset</c> would publish for a new PLN cash asset, delivered over the shared broker to this host's consumer.</summary>
    private async Task PublishCashAsync(Guid userId, Guid portfolioId, decimal amount, CancellationToken cancellationToken)
    {
        var bus = Factory.Services.GetRequiredService<IBus>();
        await bus.Publish(new AssetPositionChanged
        {
            AssetId = Guid.NewGuid(),
            PortfolioId = portfolioId,
            UserId = userId,
            AssetClass = AssetClass.Cash,
            ValuationMode = AssetValuationMode.CurrencyValued,
            Currency = "PLN",
            Quantity = amount,
            PortfolioIsArchived = false,
            Version = 0,
            OccurredAtUtc = DateTimeOffset.UtcNow,
        }, cancellationToken);
    }
}
