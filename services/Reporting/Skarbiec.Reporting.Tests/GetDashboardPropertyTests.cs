using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Reporting.Features.GetDashboard;
using Skarbiec.Reporting.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Reporting.Tests.Fixtures.ReportingApi;

namespace Skarbiec.Reporting.Tests;

public sealed class GetDashboardPropertyTests(SkarbiecContainersFixture containers) : ReportingEndpointTests(containers)
{
    private const int CaseCount = 12;

    [Fact]
    public async Task ByGroupSumsToTotal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDate = new DateOnly(2026, 8, 1);

        for (var seed = 0; seed < CaseCount; seed++)
        {
            var random = new Random(seed);
            var userId = Guid.NewGuid();
            var groups = Enumerable.Range(0, random.Next(1, 4)).Select(_ => Guid.NewGuid()).ToArray();
            var assetCount = random.Next(1, 9);
            var total = 0m;

            await using (var db = CreateDbContext(userId))
            {
                var lines = new List<(Guid AssetId, decimal Value)>();
                for (var i = 0; i < assetCount; i++)
                {
                    var assetId = Guid.NewGuid();
                    var value = random.Next(1, 100_000) / 100m;
                    var groupId = random.Next(groups.Length + 1) is var pick && pick < groups.Length ? groups[pick] : (Guid?)null;
                    total += value;
                    lines.Add((assetId, value));
                    await db.SeedPositionAsync(assetId, userId, null, cancellationToken, groupId: groupId);
                }

                await db.SeedSnapshotAsync(userId, snapshotDate, total, cancellationToken);
                foreach (var (assetId, value) in lines)
                {
                    await db.SeedValuationLineAsync(userId, null, assetId, snapshotDate, value, cancellationToken);
                }
            }

            using var client = Factory.CreateAuthenticatedClient(userId);
            var response = await client.GetAsync(DashboardUri, cancellationToken);
            var body = await response.Content.ReadFromJsonAsync<DashboardResponse>(cancellationToken);

            Assert.Equal(total, body!.NetWorthPln);
            Assert.Equal(body.NetWorthPln, body.ByGroup.Sum(g => g.ValuePln));
        }
    }
}
