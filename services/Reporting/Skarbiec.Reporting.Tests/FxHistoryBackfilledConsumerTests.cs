using MassTransit;
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

// Builds its own provider, with no HTTP host, on a queue unique to this class.
public sealed class FxHistoryBackfilledConsumerTests(SkarbiecContainersFixture containers) : IAsyncLifetime, IClassFixture<SkarbiecContainersFixture>
{
    private const string QueueName = "fx-history-backfilled-consumer-test";

    public async ValueTask InitializeAsync() => await MigrateAndResetAsync(containers);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task RequestsRebuildPerUser()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userAId = Guid.NewGuid();
        var userBId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(Guid.NewGuid(), userAId, Guid.NewGuid(), cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "USD",
                quantity: 10m, quantityHistory: [(today.AddDays(-100), 10m)]);
            await db.SeedPositionAsync(Guid.NewGuid(), userBId, Guid.NewGuid(), cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "USD",
                quantity: 5m, quantityHistory: [(today.AddDays(-55), 5m)]);
        }

        await RunConsumerAsync(async provider =>
        {
            await provider.GetRequiredService<IBus>().Publish(BackfilledEvent(today), cancellationToken);

            var requestA = await WaitForRebuildRequestAsync(provider, userAId, cancellationToken);
            Assert.Equal(today.AddDays(-60), requestA.FromDate);
            Assert.Equal(userAId, requestA.UserId);

            var requestB = await WaitForRebuildRequestAsync(provider, userBId, cancellationToken);
            Assert.Equal(today.AddDays(-55), requestB.FromDate);
            Assert.Equal(userBId, requestB.UserId);
        }, cancellationToken);
    }

    [Fact]
    public async Task PositionStartingAfterRange_Ignored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var lateUserId = Guid.NewGuid();
        var earlyUserId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(Guid.NewGuid(), lateUserId, Guid.NewGuid(), cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "USD",
                quantity: 10m, quantityHistory: [(today.AddDays(-5), 10m)]);
            await db.SeedPositionAsync(Guid.NewGuid(), earlyUserId, Guid.NewGuid(), cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "USD",
                quantity: 5m, quantityHistory: [(today.AddDays(-100), 5m)]);
        }

        await RunConsumerAsync(async provider =>
        {
            await provider.GetRequiredService<IBus>().Publish(BackfilledEvent(today), cancellationToken);

            // The earlier holding is the witness that the consumer has run; the later one must not be requested in the same pass.
            await WaitForRebuildRequestAsync(provider, earlyUserId, cancellationToken);

            Assert.Null(await GetRebuildRequestAsync(containers, lateUserId, cancellationToken));
        }, cancellationToken);
    }

    [Fact]
    public async Task Redelivery_IsIdempotent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(Guid.NewGuid(), userId, Guid.NewGuid(), cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "USD",
                quantity: 10m, quantityHistory: [(today.AddDays(-100), 10m)]);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            var messageId = Guid.NewGuid();
            var @event = BackfilledEvent(today);

            await bus.Publish(@event, ctx => ctx.MessageId = messageId, cancellationToken);
            await WaitForRebuildRequestAsync(provider, userId, cancellationToken);

            await bus.Publish(@event, ctx => ctx.MessageId = messageId, cancellationToken);

            // Nothing signals a skip, so give a redelivery time to land before asserting it never did.
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            var request = await GetRebuildRequestAsync(containers, userId, cancellationToken);
            Assert.NotNull(request);
            Assert.Equal(1L, request.Revision);
        }, cancellationToken);
    }

    private static FxHistoryBackfilled BackfilledEvent(DateOnly today) => new()
    {
        From = today.AddDays(-60),
        To = today.AddDays(-50),
        OccurredAtUtc = DateTimeOffset.UtcNow,
    };

    private Task RunConsumerAsync(Func<ServiceProvider, Task> action, CancellationToken cancellationToken) =>
        RunAsync(
            containers,
            x => x.AddConsumer<FxHistoryBackfilledConsumer>(typeof(TestConsumerDefinition)),
            action,
            cancellationToken);

    private sealed class TestConsumerDefinition : IdempotentConsumerDefinition<FxHistoryBackfilledConsumer, ReportingDbContext>
    {
        public TestConsumerDefinition() => Endpoint(e => e.Name = QueueName);
    }
}
