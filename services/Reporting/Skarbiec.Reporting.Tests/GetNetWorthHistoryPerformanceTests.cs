using System.Diagnostics;
using Skarbiec.Contracts;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Features.GetNetWorthHistory;
using Skarbiec.Reporting.Tests.Fixtures;
using Skarbiec.Reporting.Valuation;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Reporting.Tests;

/// <summary>
/// T2.12 scope: "P95 &lt;1s success criterion — measure with seeded year of snapshots (index on
/// (UserId, date))". Measured against <see cref="GetNetWorthHistoryHandler"/> directly (DB round
/// trips only), same rationale as MarketData's <c>SearchInstrumentsPerformanceTests</c> — a full
/// Kestrel/JSON round trip would mix in overhead this isn't about. A warm-up call absorbs first-query
/// JIT/connection-pool cost before the timed call.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class GetNetWorthHistoryPerformanceTests(SkarbiecContainersFixture containers) : ReportingEndpointTests(containers)
{
    [Fact]
    public async Task HandleAsync_OnSeededYearAcrossThreePortfolios_CompletesUnderOneSecond()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var portfolioIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // AddRange + one SaveChangesAsync instead of the SeedSnapshotAsync helper's per-row round
        // trip — 1095 rows one at a time would make the arrange step dwarf what this test actually
        // measures.
        await using (var seedDb = CreateDbContext(userId))
        {
            var breakdownJson = ValuationBreakdown.Serialize([new AssetClassBreakdownEntry(AssetClass.Cash, 1000m)]);

            for (var i = 0; i < 365; i++)
            {
                var date = today.AddDays(-i);
                foreach (var portfolioId in portfolioIds)
                {
                    seedDb.ValuationSnapshots.Add(new ValuationSnapshot
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        PortfolioId = portfolioId,
                        Date = date,
                        TotalPln = 1000m + i,
                        BreakdownJson = breakdownJson,
                        IsStale = false,
                    });
                }
            }

            await seedDb.SaveChangesAsync(cancellationToken);
        }

        await using var queryDb = CreateDbContext(userId);
        var handler = new GetNetWorthHistoryHandler(queryDb, TimeProvider.System);

        await handler.HandleAsync("MAX", null, cancellationToken); // warm-up: absorb JIT/connection-pool cost.

        var stopwatch = Stopwatch.StartNew();
        var result = await handler.HandleAsync("MAX", null, cancellationToken);
        stopwatch.Stop();

        Assert.True(result.IsSuccess);
        Assert.Equal(365, result.Value.Points.Count);
        Assert.True(
            stopwatch.ElapsedMilliseconds < 1000,
            $"GetNetWorthHistory took {stopwatch.ElapsedMilliseconds} ms on a seeded year across 3 portfolios, expected < 1000 ms.");
    }
}
