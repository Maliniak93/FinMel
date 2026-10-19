using System.Net.Http.Json;
using Skarbiec.Reporting.Features.GetDashboard;
using Skarbiec.Reporting.Features.GetNetWorthHistory;
using Skarbiec.Reporting.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Reporting.Tests.Fixtures.ReportingApi;

namespace Skarbiec.Reporting.Tests;

public sealed class GroupSplitTenancyTests(SkarbiecContainersFixture containers) : ReportingEndpointTests(containers)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task OtherUsersGroupsNeverAppearInSplitOrSeries()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userAId = Guid.NewGuid();
        var userBId = Guid.NewGuid();
        var groupA = Guid.NewGuid();
        var assetA = Guid.NewGuid();
        var assetB = Guid.NewGuid();
        var today = Today;

        await using (var db = CreateDbContext(userAId))
        {
            await db.SeedPositionAsync(assetA, userAId, null, cancellationToken, groupId: groupA);
            await db.SeedSnapshotAsync(userAId, today, 5_000m, cancellationToken);
            await db.SeedValuationLineAsync(userAId, null, assetA, today, 5_000m, cancellationToken);
        }

        await using (var db = CreateDbContext(userBId))
        {
            await db.SeedPositionAsync(assetB, userBId, null, cancellationToken);
            await db.SeedSnapshotAsync(userBId, today, 70m, cancellationToken);
            await db.SeedValuationLineAsync(userBId, null, assetB, today, 70m, cancellationToken);
        }

        using var client = Factory.CreateAuthenticatedClient(userBId);

        var dashboardResponse = await client.GetAsync(DashboardUri, cancellationToken);
        var dashboard = await dashboardResponse.Content.ReadFromJsonAsync<DashboardResponse>(cancellationToken);
        var entry = Assert.Single(dashboard!.ByGroup);
        Assert.Null(entry.GroupId);
        Assert.Equal(70m, entry.ValuePln);

        var groupedResponse = await client.GetAsync(NetWorthHistoryUri("MAX", groupId: groupA), cancellationToken);
        var grouped = await groupedResponse.Content.ReadFromJsonAsync<NetWorthHistoryResponse>(cancellationToken);
        Assert.All(grouped!.Points, p => Assert.Equal(0m, p.NetWorthPln));

        var ungroupedResponse = await client.GetAsync(NetWorthHistoryUri("MAX", ungrouped: true), cancellationToken);
        var ungrouped = await ungroupedResponse.Content.ReadFromJsonAsync<NetWorthHistoryResponse>(cancellationToken);
        Assert.Equal(70m, Assert.Single(ungrouped!.Points).NetWorthPln);
    }
}
