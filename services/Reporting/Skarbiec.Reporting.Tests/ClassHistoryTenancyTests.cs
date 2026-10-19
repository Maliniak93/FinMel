using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Messaging;
using Skarbiec.Reporting.Tests.Fixtures;
using Skarbiec.ServiceDefaults.Messaging;
using Skarbiec.Testing.Containers;
using static Skarbiec.Reporting.Tests.Fixtures.ReportingConsumers;

namespace Skarbiec.Reporting.Tests;

// Builds its own provider, with no HTTP host, on queues unique to this class.
public sealed class ClassHistoryTenancyTests(SkarbiecContainersFixture containers) : IAsyncLifetime, IClassFixture<SkarbiecContainersFixture>
{
    public async ValueTask InitializeAsync() => await MigrateAndResetAsync(containers);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task TimelineChange_LeavesOtherUserUntouched()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userAId = Guid.NewGuid();
        var userBId = Guid.NewGuid();
        var accountAId = Guid.NewGuid();
        var accountBId = Guid.NewGuid();
        var assetAId = Guid.NewGuid();
        var assetBId = Guid.NewGuid();
        var history = new[] { (today.AddDays(-20), 100m) };

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetAId, userAId, accountAId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, quantity: 100m,
                quantityHistory: history, classHistory: [(today.AddDays(-20), AssetClass.Cash)]);

            await db.SeedPositionAsync(assetBId, userBId, accountBId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, quantity: 100m,
                quantityHistory: history, classHistory: [(today.AddDays(-20), AssetClass.Cash)]);
            await db.SeedValuationLineAsync(userBId, accountBId, assetBId, today.AddDays(-3), 777m, cancellationToken);
            await db.SeedSnapshotAsync(userBId, today.AddDays(-3), 777m, cancellationToken);
        }

        await RunAsync(
            containers,
            x =>
            {
                x.AddConsumer<AssetPositionChangedConsumer>(typeof(PositionChangedDefinition));
                x.AddConsumer<HistoryRebuildConsumer>(typeof(RebuildDefinition));
            },
            async provider =>
            {
                await provider.GetRequiredService<IBus>().Publish(
                    new AssetPositionChanged
                    {
                        AssetId = assetAId,
                        AccountId = accountAId,
                        UserId = userAId,
                        AssetClass = AssetClass.Etf,
                        ValuationMode = AssetValuationMode.CurrencyValued,
                        Currency = "PLN",
                        Quantity = 100m,
                        QuoteUnitsPerQuantity = 1m,
                        ClassHistory =
                        [
                            new ClassPoint { Date = today.AddDays(-20), AssetClass = AssetClass.Cash },
                            new ClassPoint { Date = today.AddDays(-5), AssetClass = AssetClass.Etf },
                        ],
                        QuantityHistory = [.. history.Select(p => new QuantityPoint { Date = p.Item1, Quantity = p.Item2 })],
                        IsArchived = false,
                        Version = 1,
                        OccurredAtUtc = DateTimeOffset.UtcNow,
                    },
                    cancellationToken);

                await WaitForAsync(
                    provider,
                    (db, ct) => db.ValuationSnapshots.IgnoreQueryFilters().CountAsync(s => s.UserId == userAId && s.Date < today, ct),
                    count => count == 5,
                    "The rebuilt snapshots of user A",
                    cancellationToken);
                await WaitForNoRebuildRequestAsync(provider, userAId, cancellationToken);

                var linesA = await GetAllLinesAsync(containers, accountAId, cancellationToken);
                Assert.All(linesA.Where(l => l.Date < today), l => Assert.Equal(AssetClass.Etf, l.AssetClass));
                Assert.Equal(5, linesA.Count(l => l.Date < today));

                var lineB = Assert.Single(await GetAllLinesAsync(containers, accountBId, cancellationToken));
                Assert.Equal(AssetClass.Cash, lineB.AssetClass);
                Assert.Equal(777m, lineB.ValuePln);
                Assert.Equal(777m, Assert.Single(await GetAllSnapshotsAsync(containers, userBId, cancellationToken)).TotalPln);
                Assert.Null(await GetRebuildRequestAsync(containers, userBId, cancellationToken));

                var positionB = await WaitForPositionAsync(provider, assetBId, cancellationToken);
                Assert.Equal(AssetClass.Cash, positionB.AssetClass);
                Assert.Equal(AssetClass.Cash, Assert.Single(positionB.ClassHistory).AssetClass);
            },
            cancellationToken);
    }

    private sealed class PositionChangedDefinition : IdempotentConsumerDefinition<AssetPositionChangedConsumer, ReportingDbContext>
    {
        public PositionChangedDefinition() => Endpoint(e => e.Name = "class-history-tenancy-position-changed-test");
    }

    private sealed class RebuildDefinition : IdempotentConsumerDefinition<HistoryRebuildConsumer, ReportingDbContext>
    {
        public RebuildDefinition() => Endpoint(e => e.Name = "class-history-tenancy-rebuild-test");
    }
}
