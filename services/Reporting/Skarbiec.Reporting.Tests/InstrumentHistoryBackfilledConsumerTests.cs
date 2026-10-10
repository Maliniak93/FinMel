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
public sealed class InstrumentHistoryBackfilledConsumerTests(SkarbiecContainersFixture containers) : IAsyncLifetime, IClassFixture<SkarbiecContainersFixture>
{
    private const string QueueName = "instrument-history-backfilled-consumer-test";

    public async ValueTask InitializeAsync() => await MigrateAndResetAsync(containers);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Consume_RequestsFromEachHoldingsStart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var instrumentXId = Guid.NewGuid();
        var instrumentYId = Guid.NewGuid();
        var userAId = Guid.NewGuid();
        var portfolioAId = Guid.NewGuid();
        var userBId = Guid.NewGuid();
        var portfolioBId = Guid.NewGuid();
        var userCId = Guid.NewGuid();
        var portfolioCId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(Guid.NewGuid(), userAId, portfolioAId, cancellationToken,
                assetClass: AssetClass.Stock, valuationMode: AssetValuationMode.Market, instrumentId: instrumentXId,
                currency: "PLN", quantity: 10m, quantityHistory: [(today.AddDays(-100), 10m)]);
            await db.SeedPositionAsync(Guid.NewGuid(), userBId, portfolioBId, cancellationToken,
                assetClass: AssetClass.Stock, valuationMode: AssetValuationMode.Market, instrumentId: instrumentXId,
                currency: "PLN", quantity: 5m, quantityHistory: [(today.AddDays(-60), 5m)]);
            await db.SeedPositionAsync(Guid.NewGuid(), userCId, portfolioCId, cancellationToken,
                assetClass: AssetClass.Stock, valuationMode: AssetValuationMode.Market, instrumentId: instrumentYId,
                currency: "PLN", quantity: 3m, quantityHistory: [(today.AddDays(-100), 3m)]);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(BackfilledEvent(instrumentXId, today), cancellationToken);

            var requestA = await WaitForRebuildRequestAsync(provider, portfolioAId, cancellationToken);
            Assert.Equal(today.AddDays(-100), requestA.FromDate);
            Assert.Equal(userAId, requestA.UserId);

            var requestB = await WaitForRebuildRequestAsync(provider, portfolioBId, cancellationToken);
            Assert.Equal(today.AddDays(-60), requestB.FromDate);
            Assert.Equal(userBId, requestB.UserId);

            Assert.Null(await GetRebuildRequestAsync(containers, portfolioCId, cancellationToken));
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_HoldingStartsAfterTo_RequestsNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var instrumentId = Guid.NewGuid();
        var lateUserId = Guid.NewGuid();
        var latePortfolioId = Guid.NewGuid();
        var earlyUserId = Guid.NewGuid();
        var earlyPortfolioId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(Guid.NewGuid(), lateUserId, latePortfolioId, cancellationToken,
                assetClass: AssetClass.Stock, valuationMode: AssetValuationMode.Market, instrumentId: instrumentId,
                currency: "PLN", quantity: 10m, quantityHistory: [(today.AddDays(-40), 10m)]);
            await db.SeedPositionAsync(Guid.NewGuid(), earlyUserId, earlyPortfolioId, cancellationToken,
                assetClass: AssetClass.Stock, valuationMode: AssetValuationMode.Market, instrumentId: instrumentId,
                currency: "PLN", quantity: 5m, quantityHistory: [(today.AddDays(-100), 5m)]);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(BackfilledEvent(instrumentId, today), cancellationToken);

            // The earlier holding is the witness that the consumer has run; the later one must not be requested in the same pass.
            await WaitForRebuildRequestAsync(provider, earlyPortfolioId, cancellationToken);

            Assert.Null(await GetRebuildRequestAsync(containers, latePortfolioId, cancellationToken));
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_SameMessageIdTwice_AppliesOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var instrumentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(Guid.NewGuid(), userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Stock, valuationMode: AssetValuationMode.Market, instrumentId: instrumentId,
                currency: "PLN", quantity: 10m, quantityHistory: [(today.AddDays(-100), 10m)]);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            var messageId = Guid.NewGuid();
            var @event = BackfilledEvent(instrumentId, today);

            await bus.Publish(@event, ctx => ctx.MessageId = messageId, cancellationToken);
            await WaitForRebuildRequestAsync(provider, portfolioId, cancellationToken);

            // Same MessageId: the inbox must skip the consumer body, so the revision is not bumped again.
            await bus.Publish(@event, ctx => ctx.MessageId = messageId, cancellationToken);

            // Nothing signals a skip, so give a redelivery time to land before asserting it never did.
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            var request = await GetRebuildRequestAsync(containers, portfolioId, cancellationToken);
            Assert.NotNull(request);
            Assert.Equal(1L, request.Revision);
        }, cancellationToken);
    }

    private static InstrumentHistoryBackfilled BackfilledEvent(Guid instrumentId, DateOnly today) => new()
    {
        InstrumentId = instrumentId,
        From = today.AddDays(-365),
        To = today.AddDays(-50),
        OccurredAtUtc = DateTimeOffset.UtcNow,
    };

    private Task RunConsumerAsync(Func<ServiceProvider, Task> action, CancellationToken cancellationToken) =>
        RunAsync(
            containers,
            x => x.AddConsumer<InstrumentHistoryBackfilledConsumer>(typeof(TestConsumerDefinition)),
            action,
            cancellationToken);

    private sealed class TestConsumerDefinition : IdempotentConsumerDefinition<InstrumentHistoryBackfilledConsumer, ReportingDbContext>
    {
        public TestConsumerDefinition() => Endpoint(e => e.Name = QueueName);
    }
}
