using System.Diagnostics;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Features.GetNetWorthHistory;
using Skarbiec.Reporting.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Reporting.Tests;

// Timed against the handler, not over HTTP, after a warm-up call absorbs JIT and connection-pool cost.
[Collection(TestingDefaults.SerialCollectionName)]
public sealed class GetNetWorthHistoryPerformanceTests(SkarbiecContainersFixture containers) : ReportingEndpointTests(containers)
{
    [Fact]
    public async Task HandleAsync_OnSeededYearAcrossThreePortfolios_CompletesUnderOneSecond()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var portfolioIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // One SaveChangesAsync instead of SeedSnapshotAsync per row, so the arrange step does not dwarf the measurement.
        await using (var seedDb = CreateDbContext(userId))
        {
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
                        IsStale = false,
                    });
                }
            }

            await seedDb.SaveChangesAsync(cancellationToken);
        }

        await using var queryDb = CreateDbContext(userId);
        var handler = new GetNetWorthHistoryHandler(queryDb, TimeProvider.System);

        await handler.HandleAsync("MAX", null, cancellationToken);

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
