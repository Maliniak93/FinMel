using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Messaging;
using Skarbiec.ServiceDefaults.Authentication;
using Skarbiec.ServiceDefaults.Messaging;
using Skarbiec.ServiceDefaults.Tenancy;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Reporting.Tests;

/// <summary>
/// <c>AssetPositionChanged</c> in, <see cref="Position"/> upserted by <c>AssetId</c> — idempotently
/// and never regressed by an out-of-order redelivery (spec-03 AC1-4). Builds its own provider (no
/// HTTP host needed — the consumer only touches <see cref="ReportingDbContext"/>) on a queue name
/// unique to this test class, mirroring <c>DailyPricesSyncedConsumerTests</c>.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class AssetPositionChangedConsumerTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private const string QueueName = "asset-position-changed-consumer-test";

    public async ValueTask InitializeAsync()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReportingDbContext>().Database.MigrateAsync();

        await containers.ResetDatabaseAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Consume_NewAsset_InsertsPositionWithFullState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var assetId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var instrumentId = Guid.NewGuid();

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(new AssetPositionChanged
            {
                AssetId = assetId,
                PortfolioId = portfolioId,
                UserId = userId,
                AssetClass = AssetClass.Stock,
                ValuationMode = AssetValuationMode.Market,
                InstrumentId = instrumentId,
                Currency = "USD",
                Quantity = 12m,
                ManualValueAmount = null,
                ManualValueDate = null,
                PortfolioIsArchived = false,
                Version = 0,
                OccurredAtUtc = DateTimeOffset.UtcNow,
            }, cancellationToken);

            var position = await WaitForPositionAsync(provider, assetId, cancellationToken);

            Assert.Equal(portfolioId, position.PortfolioId);
            Assert.Equal(userId, position.UserId);
            Assert.Equal(AssetClass.Stock, position.AssetClass);
            Assert.Equal(AssetValuationMode.Market, position.ValuationMode);
            Assert.Equal(instrumentId, position.InstrumentId);
            Assert.Equal("USD", position.Currency);
            Assert.Equal(12m, position.Quantity);
            Assert.Null(position.ManualValueAmount);
            Assert.Null(position.ManualValueDate);
            Assert.False(position.PortfolioIsArchived);
            Assert.Equal(0, position.Version);
            Assert.True(position.UpdatedAt > DateTimeOffset.MinValue);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_SecondEventForSameAsset_UpsertsSingleRow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var assetId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();

            await bus.Publish(Event(assetId, portfolioId, userId, quantity: 5m, version: 0), cancellationToken);
            await WaitForPositionAsync(provider, assetId, cancellationToken);

            await bus.Publish(Event(assetId, portfolioId, userId, quantity: 9m, version: 1), cancellationToken);
            var position = await WaitForPositionAsync(provider, assetId, cancellationToken, p => p.Version == 1);

            Assert.Equal(9m, position.Quantity);
            Assert.Equal(1, position.Version);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            var count = await db.Positions.IgnoreQueryFilters().CountAsync(p => p.AssetId == assetId, cancellationToken);
            Assert.Equal(1, count);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_LowerVersionThanStored_IgnoresStaleEvent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var assetId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();

            await bus.Publish(Event(assetId, portfolioId, userId, quantity: 100m, version: 5), cancellationToken);
            await WaitForPositionAsync(provider, assetId, cancellationToken, p => p.Version == 5);

            await bus.Publish(Event(assetId, portfolioId, userId, quantity: 1m, version: 3), cancellationToken);

            // No signal to wait on for "it was ignored" — give a genuine (mis-ordered) delivery time
            // to land before asserting the stored row was left untouched.
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            var position = await db.Positions.IgnoreQueryFilters().SingleAsync(p => p.AssetId == assetId, cancellationToken);

            Assert.Equal(5, position.Version);
            Assert.Equal(100m, position.Quantity);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_SameMessageIdTwice_AppliesOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var assetId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            var messageId = Guid.NewGuid();
            var @event = Event(assetId, portfolioId, userId, quantity: 42m, version: 0);

            await bus.Publish(@event, ctx => ctx.MessageId = messageId, cancellationToken);
            await WaitForPositionAsync(provider, assetId, cancellationToken);

            // Redeliver: same MessageId, same content. The inbox (InboxState, keyed on MessageId +
            // ConsumerId) must recognize it and skip the consumer body entirely.
            await bus.Publish(@event, ctx => ctx.MessageId = messageId, cancellationToken);

            // No signal to wait on for "it was skipped" — give a genuine redelivery time to land
            // before asserting it never did.
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            var positions = await db.Positions.IgnoreQueryFilters()
                .Where(p => p.AssetId == assetId)
                .ToListAsync(cancellationToken);

            var position = Assert.Single(positions);
            Assert.Equal(42m, position.Quantity);
        }, cancellationToken);
    }

    private static AssetPositionChanged Event(Guid assetId, Guid portfolioId, Guid userId, decimal quantity, long version) => new()
    {
        AssetId = assetId,
        PortfolioId = portfolioId,
        UserId = userId,
        AssetClass = AssetClass.Cash,
        ValuationMode = AssetValuationMode.CurrencyValued,
        Currency = "PLN",
        Quantity = quantity,
        PortfolioIsArchived = false,
        Version = version,
        OccurredAtUtc = DateTimeOffset.UtcNow,
    };

    private async Task RunConsumerAsync(Func<ServiceProvider, Task> action, CancellationToken cancellationToken)
    {
        await using var provider = BuildProvider();
        var hostedServices = provider.GetServices<IHostedService>().ToList();

        foreach (var hostedService in hostedServices)
        {
            await hostedService.StartAsync(cancellationToken);
        }

        try
        {
            // See DailyPricesSyncedConsumerTests: a publish fired immediately after StartAsync can
            // race the exchange->queue binding on a fresh queue and be dropped.
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);

            await action(provider);
        }
        finally
        {
            for (var i = hostedServices.Count - 1; i >= 0; i--)
            {
                await hostedServices[i].StopAsync(cancellationToken);
            }
        }
    }

    private static async Task<Position> WaitForPositionAsync(
        ServiceProvider provider, Guid assetId, CancellationToken cancellationToken, Func<Position, bool>? predicate = null)
    {
        predicate ??= _ => true;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            var position = await db.Positions.IgnoreQueryFilters()
                .SingleOrDefaultAsync(p => p.AssetId == assetId, cancellationToken);

            if (position is not null && predicate(position))
            {
                return position;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }

        throw new TimeoutException($"No matching Position for asset {assetId} within the deadline.");
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // No HTTP request in this bare provider — the consumer sets UserId explicitly from the
        // event and reads with IgnoreQueryFilters, but ReportingDbContext's constructor still needs
        // some ICurrentUser implementation to satisfy DI.
        services.AddSingleton<ICurrentUser, DesignTimeCurrentUser>();

        services.AddDbContext<ReportingDbContext>(options => options.UseNpgsql(containers.PostgresConnectionString));

        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();

            // No UseBusOutbox() here: this test only exercises the consumer-side inbox, never
            // IBus/IPublishEndpoint from a DI scope, so the producer-side bus outbox and its
            // background delivery poller would be dead weight.
            x.AddEntityFrameworkOutbox<ReportingDbContext>(o => o.UsePostgres());

            x.AddConsumer<AssetPositionChangedConsumer>(typeof(TestConsumerDefinition));

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(new Uri(containers.RabbitMqConnectionString));

                cfg.ConfigureEndpoints(context);
            });
        });

        return services.BuildServiceProvider();
    }

    private sealed class TestConsumerDefinition : IdempotentConsumerDefinition<AssetPositionChangedConsumer, ReportingDbContext>
    {
        public TestConsumerDefinition() => Endpoint(e => e.Name = QueueName);
    }
}
