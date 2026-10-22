using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Messaging;
using Skarbiec.Reporting.Tests.Fixtures;
using Skarbiec.ServiceDefaults.Messaging;
using Skarbiec.Testing.Containers;
using static Skarbiec.Reporting.Tests.Fixtures.ReportingConsumers;

namespace Skarbiec.Reporting.Tests;

public sealed class LatestValuationPublishTests(SkarbiecContainersFixture containers) : IAsyncLifetime, IClassFixture<SkarbiecContainersFixture>
{
    private const string QueueName = "latest-valuation-publish-test";

    public async ValueTask InitializeAsync() => await MigrateAndResetAsync(containers);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task PublishesValuesPerGroup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDate = new DateOnly(2026, 8, 10);
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(Guid.NewGuid(), userId, accountId, cancellationToken,
                assetClass: AssetClass.Cash, quantity: 1_000m, groupId: groupId);
            await db.SeedPositionAsync(Guid.NewGuid(), userId, accountId, cancellationToken,
                assetClass: AssetClass.BankAccount, quantity: 500m, groupId: groupId);
            await db.SeedPositionAsync(Guid.NewGuid(), userId, accountId, cancellationToken,
                assetClass: AssetClass.Cash, quantity: 300m);
        }

        await RunAsync(
            containers,
            x =>
            {
                x.AddConsumer<DailyPricesSyncedConsumer>(typeof(TestConsumerDefinition));
                x.AddConsumer<LatestValuationRecorderConsumer>();
            },
            async provider =>
            {
                await PublishDailyPricesSyncedAsync(provider.GetRequiredService<IBus>(), snapshotDate, cancellationToken);

                var recorder = provider.GetRequiredService<LatestValuationRecorder>();
                var published = Assert.Single(await recorder.SettledDeliveriesForAsync(userId, cancellationToken));

                Assert.Equal(1_800m, published.TotalPln);
                Assert.Equal(2, published.ByGroup.Count);

                var grouped = Assert.Single(published.ByGroup, g => g.GroupId == groupId);
                Assert.Equal(
                    new Dictionary<AssetClass, decimal> { [AssetClass.Cash] = 1_000m, [AssetClass.BankAccount] = 500m },
                    grouped.ByAssetClass.ToDictionary(c => c.AssetClass, c => c.ValuePln));

                var ungrouped = Assert.Single(published.ByGroup, g => g.GroupId is null);
                var cash = Assert.Single(ungrouped.ByAssetClass);
                Assert.Equal(AssetClass.Cash, cash.AssetClass);
                Assert.Equal(300m, cash.ValuePln);
            },
            cancellationToken);
    }

    private sealed class TestConsumerDefinition : IdempotentConsumerDefinition<DailyPricesSyncedConsumer, ReportingDbContext>
    {
        public TestConsumerDefinition() => Endpoint(e => e.Name = QueueName);
    }
}
