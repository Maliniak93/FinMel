using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources.MfBonds;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.MarketData.Tests.Fixtures.PriceSources;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

public sealed class BondCatalogSyncJobTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    private static BondCatalogSyncJob JobFor(MarketDataDbContext db, IMfBondSource source) =>
        new(db, source, TimeProvider.System, NullLogger<BondCatalogSyncJob>.Instance);

    private async Task<(List<string> Series, List<string> Rates)> SnapshotAsync(CancellationToken cancellationToken)
    {
        await using var db = CreateDbContext();
        var series = (await db.BondSeries.AsNoTracking().OrderBy(s => s.Code).ToListAsync(cancellationToken))
            .Select(s => $"{s.Code}|{s.Type}|{s.Isin}|{s.SaleStart}|{s.SaleEnd}|{s.IssuePrice}|{s.SwapPrice}|{s.MarginPercent}")
            .ToList();
        var rates = (await db.BondSeriesPeriodRates.AsNoTracking().OrderBy(r => r.SeriesCode).ThenBy(r => r.PeriodIndex).ToListAsync(cancellationToken))
            .Select(r => $"{r.SeriesCode}|{r.PeriodIndex}|{r.RatePercent}")
            .ToList();
        return (series, rates);
    }

    [Fact]
    public async Task Run_Twice_UpsertsIdempotently()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using (var db = CreateDbContext())
        {
            await db.SeedBondSeriesAsync("ROR0623", TreasuryBondType.Ror, new DateOnly(2000, 1, 1), new DateOnly(2000, 1, 31), cancellationToken);
            db.BondSeriesPeriodRates.AddRange(Enumerable.Range(0, 11).Select(i => new BondSeriesPeriodRate { SeriesCode = "ROR0623", PeriodIndex = i, RatePercent = 1m }));
            await db.SaveChangesAsync(cancellationToken);
        }

        await using (var db = CreateDbContext())
        {
            await JobFor(db, FakeMfBondSource.FromFixture()).RunAsync(cancellationToken);
        }

        var afterFirst = await SnapshotAsync(cancellationToken);

        await using (var db = CreateDbContext())
        {
            await JobFor(db, FakeMfBondSource.FromFixture()).RunAsync(cancellationToken);
        }

        var afterSecond = await SnapshotAsync(cancellationToken);

        Assert.Equal(afterFirst.Series.Count, afterFirst.Series.Select(s => s.Split('|')[0]).Distinct().Count());
        Assert.Equal(afterFirst.Series, afterSecond.Series);
        Assert.Equal(afterFirst.Rates, afterSecond.Rates);

        await using var check = CreateDbContext();
        var rorRates = await check.BondSeriesPeriodRates.AsNoTracking()
            .Where(r => r.SeriesCode == "ROR0623")
            .OrderBy(r => r.PeriodIndex)
            .Select(r => r.RatePercent)
            .ToListAsync(cancellationToken);
        Assert.Equal([5.25m, 6.00m, 6.50m, 6.50m, .. Enumerable.Repeat(6.75m, 8)], rorRates);

        var runs = await check.SyncRuns.AsNoTracking().Where(r => r.Kind == SyncRunKind.BondCatalog).ToListAsync(cancellationToken);
        Assert.Equal(2, runs.Count);
        Assert.All(runs, r => Assert.Equal(SyncRunStatus.Completed, r.Status));
    }

    [Fact]
    public async Task Run_SourceFails_KeepsCatalog()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using (var db = CreateDbContext())
        {
            await db.SeedBondCatalogFromFixtureAsync(cancellationToken);
        }

        var before = await SnapshotAsync(cancellationToken);
        Assert.NotEmpty(before.Series);

        IMfBondSource[] failingSources =
        [
            FakeMfBondSource.ThrowingOnRequest(new HttpRequestException("page unreachable")),
            FakeMfBondSource.ThrowingOnRequest(new InvalidOperationException("no link to the file on the page")),
            FakeMfBondSource.WithFile([1, 2, 3, 4, 5, 6, 7, 8]),
        ];

        foreach (var source in failingSources)
        {
            await using var db = CreateDbContext();
            await JobFor(db, source).RunAsync(cancellationToken);
        }

        var after = await SnapshotAsync(cancellationToken);
        Assert.Equal(before.Series, after.Series);
        Assert.Equal(before.Rates, after.Rates);

        await using var check = CreateDbContext();
        var failed = await check.SyncRuns.AsNoTracking()
            .CountAsync(r => r.Kind == SyncRunKind.BondCatalog && r.Status == SyncRunStatus.Failed, cancellationToken);
        Assert.Equal(failingSources.Length, failed);
    }
}
