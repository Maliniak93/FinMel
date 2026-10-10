using System.Diagnostics;
using Skarbiec.Contracts;
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

        await handler.HandleAsync("MAX", null, null, cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        var result = await handler.HandleAsync("MAX", null, null, cancellationToken);
        stopwatch.Stop();

        Assert.True(result.IsSuccess);
        Assert.Equal(365, result.Value.Points.Count);
        Assert.True(
            stopwatch.ElapsedMilliseconds < 1000,
            $"GetNetWorthHistory took {stopwatch.ElapsedMilliseconds} ms on a seeded year across 3 portfolios, expected < 1000 ms.");
    }

    [Fact]
    public async Task HandleAsync_WithAssetClass_OnSeededYearAcrossThreePortfolios_CompletesUnderOneSecond()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var portfolioIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var etfAssetIds = portfolioIds.Select(_ => Guid.NewGuid()).ToArray();
        var cashAssetIds = portfolioIds.Select(_ => Guid.NewGuid()).ToArray();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await using (var seedDb = CreateDbContext(userId))
        {
            for (var i = 0; i < 365; i++)
            {
                var date = today.AddDays(-i);
                for (var p = 0; p < portfolioIds.Length; p++)
                {
                    seedDb.ValuationSnapshots.Add(new ValuationSnapshot
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        PortfolioId = portfolioIds[p],
                        Date = date,
                        TotalPln = 1000m + i + 500m,
                        IsStale = false,
                    });
                    seedDb.AssetValuations.Add(NewLine(userId, portfolioIds[p], etfAssetIds[p], date, 1000m + i, AssetClass.Etf));
                    seedDb.AssetValuations.Add(NewLine(userId, portfolioIds[p], cashAssetIds[p], date, 500m, AssetClass.Cash));
                }
            }

            await seedDb.SaveChangesAsync(cancellationToken);
        }

        await using var queryDb = CreateDbContext(userId);
        var handler = new GetNetWorthHistoryHandler(queryDb, TimeProvider.System);

        await handler.HandleAsync("MAX", null, AssetClass.Etf, cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        var result = await handler.HandleAsync("MAX", null, AssetClass.Etf, cancellationToken);
        stopwatch.Stop();

        Assert.True(result.IsSuccess);
        Assert.Equal(365, result.Value.Points.Count);
        Assert.True(
            stopwatch.ElapsedMilliseconds < 1000,
            $"GetNetWorthHistory with an asset class took {stopwatch.ElapsedMilliseconds} ms on a seeded year across 3 portfolios, expected < 1000 ms.");
    }

    private static AssetValuation NewLine(Guid userId, Guid portfolioId, Guid assetId, DateOnly date, decimal valuePln, AssetClass assetClass) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        PortfolioId = portfolioId,
        AssetId = assetId,
        Date = date,
        AssetClass = assetClass,
        Quantity = 1m,
        ValuePln = valuePln,
        IsStale = false,
    };
}
