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
public sealed class OutsideNetWorthTenancyTests(SkarbiecContainersFixture containers) : IAsyncLifetime, IClassFixture<SkarbiecContainersFixture>
{
    public async ValueTask InitializeAsync() => await MigrateAndResetAsync(containers);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task FlagChange_DoesNotTouchOtherUser()
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
            await db.SeedValuationLineAsync(userAId, accountAId, assetAId, today.AddDays(-3), 100m, cancellationToken, quantity: 100m);
            await db.SeedSnapshotAsync(userAId, today.AddDays(-3), 100m, cancellationToken);

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
                        AssetClass = AssetClass.Cash,
                        ValuationMode = AssetValuationMode.CurrencyValued,
                        Currency = "PLN",
                        Quantity = 100m,
                        QuoteUnitsPerQuantity = 1m,
                        ClassHistory = [new ClassPoint { Date = today.AddDays(-20), AssetClass = AssetClass.Cash }],
                        QuantityHistory = [.. history.Select(p => new QuantityPoint { Date = p.Item1, Quantity = p.Item2 })],
                        IsArchived = false,
                        OutsideNetWorth = true,
                        Version = 1,
                        OccurredAtUtc = DateTimeOffset.UtcNow,
                    },
                    cancellationToken);

                await WaitForPositionAsync(provider, assetAId, cancellationToken, p => p.OutsideNetWorth);
                await WaitForNoRebuildRequestAsync(provider, userAId, cancellationToken);

                Assert.Empty(await GetAllLinesAsync(containers, accountAId, cancellationToken));
                Assert.DoesNotContain(await GetAllSnapshotsAsync(containers, userAId, cancellationToken), s => s.Date < today);

                var lineB = Assert.Single(await GetAllLinesAsync(containers, accountBId, cancellationToken));
                Assert.Equal(777m, lineB.ValuePln);
                Assert.Equal(777m, Assert.Single(await GetAllSnapshotsAsync(containers, userBId, cancellationToken)).TotalPln);
                Assert.Null(await GetRebuildRequestAsync(containers, userBId, cancellationToken));

                var positionB = await WaitForPositionAsync(provider, assetBId, cancellationToken);
                Assert.False(positionB.OutsideNetWorth);
            },
            cancellationToken);
    }

    private sealed class PositionChangedDefinition : IdempotentConsumerDefinition<AssetPositionChangedConsumer, ReportingDbContext>
    {
        public PositionChangedDefinition() => Endpoint(e => e.Name = "outside-net-worth-tenancy-position-changed-test");
    }

    private sealed class RebuildDefinition : IdempotentConsumerDefinition<HistoryRebuildConsumer, ReportingDbContext>
    {
        public RebuildDefinition() => Endpoint(e => e.Name = "outside-net-worth-tenancy-rebuild-test");
    }
}
