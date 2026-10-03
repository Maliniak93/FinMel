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
using static Skarbiec.Reporting.Tests.Fixtures.ReportingConsumers;

namespace Skarbiec.Reporting.Tests;

// The dashboard is called directly; each step waits for the previous consume, since the two event types sit on separate queues.
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

        await Bus.PublishCashPositionAsync(userId, portfolioId, 500m, cancellationToken);

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

        await Bus.PublishCashPositionAsync(userAId, Guid.NewGuid(), 500m, cancellationToken);

        // User A's dashboard reaching 500 signals the event was consumed, so asserting B cannot pass vacuously.
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

    [Fact]
    public async Task ArchivePortfolio_DropsItFromNetWorth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = new ArchiveScenario(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        using var client = Factory.CreateAuthenticatedClient(scenario.UserId);
        await ArrangeTwoPortfoliosAsync(client, scenario, cancellationToken);

        await PublishArchiveFanOutAsync(scenario, cancellationToken);
        await WaitForPositionAsync(Factory.Services, scenario.ArchivedAssetId, cancellationToken, p => p.PortfolioIsArchived);
        await PublishPortfolioArchivedAsync(scenario, cancellationToken);

        using var response = await client.GetDashboardWhenAsync(d => d.NetWorthPln == 500m, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<DashboardResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(500m, body!.NetWorthPln);
    }

    [Fact]
    public async Task ArchivePortfolio_EventsOutOfOrder_DropsItFromNetWorth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = new ArchiveScenario(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        using var client = Factory.CreateAuthenticatedClient(scenario.UserId);
        await ArrangeTwoPortfoliosAsync(client, scenario, cancellationToken);

        await PublishPortfolioArchivedAsync(scenario, cancellationToken);
        await WaitForPositionAsync(Factory.Services, scenario.ArchivedAssetId, cancellationToken, p => p.PortfolioIsArchived);
        await PublishArchiveFanOutAsync(scenario, cancellationToken);
        await WaitForPositionAsync(Factory.Services, scenario.ArchivedAssetId, cancellationToken, p => p.Version == 1);

        using var response = await client.GetDashboardWhenAsync(d => d.NetWorthPln == 500m, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<DashboardResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(500m, body!.NetWorthPln);
    }

    // The intermediate 500 is asserted too, or an archive that dropped nothing would pass.
    [Fact]
    public async Task RestorePortfolio_BringsItBackIntoNetWorth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = new ArchiveScenario(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        using var client = Factory.CreateAuthenticatedClient(scenario.UserId);
        await ArrangeTwoPortfoliosAsync(client, scenario, cancellationToken);

        await PublishArchiveFanOutAsync(scenario, cancellationToken);
        await WaitForPositionAsync(Factory.Services, scenario.ArchivedAssetId, cancellationToken, p => p.PortfolioIsArchived);
        await PublishPortfolioArchivedAsync(scenario, cancellationToken);
        using (var archived = await client.GetDashboardWhenAsync(d => d.NetWorthPln == 500m, cancellationToken))
        {
            var archivedBody = await archived.Content.ReadFromJsonAsync<DashboardResponse>(cancellationToken);
            Assert.Equal(500m, archivedBody!.NetWorthPln);
        }

        // Portfolio's restore: the fan-out with the flag cleared, plus PortfolioRestored.
        await Bus.PublishCashPositionAsync(
            scenario.UserId, scenario.ArchivedPortfolioId, 1_000m, cancellationToken,
            assetId: scenario.ArchivedAssetId, portfolioIsArchived: false, version: 2);
        await Bus.Publish(new PortfolioRestored
        {
            PortfolioId = scenario.ArchivedPortfolioId,
            UserId = scenario.UserId,
            OccurredAtUtc = DateTimeOffset.UtcNow,
        }, cancellationToken);

        using var response = await client.GetDashboardWhenAsync(d => d.NetWorthPln == 1_500m, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<DashboardResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1_500m, body!.NetWorthPln);
    }

    private IBus Bus => Factory.Services.GetRequiredService<IBus>();

    private async Task ArrangeTwoPortfoliosAsync(HttpClient client, ArchiveScenario scenario, CancellationToken cancellationToken)
    {
        await client.WaitUntilReadyAsync(cancellationToken);

        await Bus.PublishCashPositionAsync(
            scenario.UserId, scenario.ArchivedPortfolioId, 1_000m, cancellationToken, assetId: scenario.ArchivedAssetId);
        await Bus.PublishCashPositionAsync(scenario.UserId, Guid.NewGuid(), 500m, cancellationToken);

        using var response = await client.GetDashboardWhenAsync(d => d.NetWorthPln == 1_500m, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<DashboardResponse>(cancellationToken);
        Assert.Equal(1_500m, body!.NetWorthPln);
    }

    private Task PublishArchiveFanOutAsync(ArchiveScenario scenario, CancellationToken cancellationToken) =>
        Bus.PublishCashPositionAsync(
            scenario.UserId, scenario.ArchivedPortfolioId, 1_000m, cancellationToken,
            assetId: scenario.ArchivedAssetId, portfolioIsArchived: true, version: 1);

    private Task PublishPortfolioArchivedAsync(ArchiveScenario scenario, CancellationToken cancellationToken) =>
        Bus.Publish(new PortfolioArchived
        {
            PortfolioId = scenario.ArchivedPortfolioId,
            UserId = scenario.UserId,
            OccurredAtUtc = DateTimeOffset.UtcNow,
        }, cancellationToken);

    private sealed record ArchiveScenario(Guid UserId, Guid ArchivedPortfolioId, Guid ArchivedAssetId);
}
