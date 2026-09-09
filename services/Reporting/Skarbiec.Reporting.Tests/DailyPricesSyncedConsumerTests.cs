using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.MarketData;
using Skarbiec.Reporting.Messaging;
using Skarbiec.Reporting.Portfolio;
using Skarbiec.Reporting.Tests.Fixtures;
using Skarbiec.Reporting.Valuation;
using Skarbiec.ServiceDefaults.Authentication;
using Skarbiec.ServiceDefaults.Messaging;
using Skarbiec.ServiceDefaults.Tenancy;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Reporting.Tests;

/// <summary>
/// End-to-end (T2.11 AC): <c>DailyPricesSynced</c> in, <see cref="ValuationSnapshot"/> rows out,
/// idempotently. Builds its own provider (rather than <c>ReportingApiFactory</c>) with fake
/// Portfolio/MarketData clients on a queue name unique to this test class — the consumer needs
/// RabbitMQ + Postgres but no HTTP host, mirroring Identity's
/// <c>UserRegisteredIdempotentConsumerTests</c>.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class DailyPricesSyncedConsumerTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private const string QueueName = "daily-prices-synced-consumer-test";

    public async ValueTask InitializeAsync()
    {
        await using var provider = BuildProvider(new FakePositionsClient(), new FakePriceQuoteClient());
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReportingDbContext>().Database.MigrateAsync();

        await containers.ResetDatabaseAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Consume_DailyPricesSynced_ComputesSnapshotsForEveryPortfolioAcrossUsers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDate = new DateOnly(2026, 8, 10);
        var stockInstrumentId = Guid.NewGuid();

        var userAId = Guid.NewGuid();
        var portfolioAId = Guid.NewGuid();
        var userBId = Guid.NewGuid();
        var portfolioBId = Guid.NewGuid();

        var positionsClient = new FakePositionsClient()
            .WithPosition(new PositionForValuation
            {
                UserId = userAId,
                PortfolioId = portfolioAId,
                AssetClass = AssetClass.Stock,
                ValuationMode = AssetValuationMode.Market,
                Currency = "PLN",
                Quantity = 10,
                InstrumentId = stockInstrumentId,
            })
            .WithPosition(new PositionForValuation
            {
                UserId = userBId,
                PortfolioId = portfolioBId,
                AssetClass = AssetClass.RealEstate,
                ValuationMode = AssetValuationMode.Manual,
                Currency = "PLN",
                Quantity = 0,
                ManualValueAmount = 300_000m,
            });

        var priceQuoteClient = new FakePriceQuoteClient()
            .WithPrice(stockInstrumentId, new InstrumentPriceLookup("PLN", snapshotDate, 150m));

        await RunConsumerAsync(positionsClient, priceQuoteClient, async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(new DailyPricesSynced
            {
                RunId = Guid.NewGuid(),
                SyncDate = snapshotDate,
                SyncedCount = 1,
                FailedCount = 0,
                NoDataCount = 0,
            }, cancellationToken);

            var snapshotA = await WaitForSnapshotAsync(provider, portfolioAId, snapshotDate, cancellationToken);
            var snapshotB = await WaitForSnapshotAsync(provider, portfolioBId, snapshotDate, cancellationToken);

            Assert.Equal(userAId, snapshotA.UserId);
            Assert.Equal(1500m, snapshotA.TotalPln); // 10 units x 150 PLN, same-day quote.
            Assert.False(snapshotA.IsStale);

            Assert.Equal(userBId, snapshotB.UserId);
            Assert.Equal(300_000m, snapshotB.TotalPln);
        }, cancellationToken);
    }

    /// <summary>M1.4 AC: a currency-valued asset (no instrument, no manual amount) flows
    /// event → snapshot with the correct PLN value — the case that valued at 0 before this mode
    /// existed as an explicit branch in <see cref="Skarbiec.Reporting.Valuation.ValuationAlgorithm"/>.</summary>
    [Fact]
    public async Task Consume_DailyPricesSynced_CurrencyValuedAssetValuesThroughFxRate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDate = new DateOnly(2026, 8, 12);
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();

        var positionsClient = new FakePositionsClient()
            .WithPosition(new PositionForValuation
            {
                UserId = userId,
                PortfolioId = portfolioId,
                AssetClass = AssetClass.Cash,
                ValuationMode = AssetValuationMode.CurrencyValued,
                Currency = "EUR",
                Quantity = 1_000m, // 1000 EUR cash — no instrument, no manual value.
            });

        var priceQuoteClient = new FakePriceQuoteClient()
            .WithFxRate("EURPLN", new FxRateLookup(snapshotDate, 4.30m));

        await RunConsumerAsync(positionsClient, priceQuoteClient, async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(new DailyPricesSynced
            {
                RunId = Guid.NewGuid(),
                SyncDate = snapshotDate,
                SyncedCount = 1,
                FailedCount = 0,
                NoDataCount = 0,
            }, cancellationToken);

            var snapshot = await WaitForSnapshotAsync(provider, portfolioId, snapshotDate, cancellationToken);

            // 1000 EUR x 4.30 PLN/EUR = 4300 PLN — not 0, which is what pre-M1.4 code would have
            // produced (it treated "no InstrumentId" as manual, and ManualValueAmount is null here).
            Assert.Equal(userId, snapshot.UserId);
            Assert.Equal(4_300m, snapshot.TotalPln);
            Assert.False(snapshot.IsStale);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_SameMessageIdDeliveredTwice_SnapshotComputedOnlyOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDate = new DateOnly(2026, 8, 11);
        var portfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var positionsClient = new FakePositionsClient()
            .WithPosition(new PositionForValuation
            {
                UserId = userId,
                PortfolioId = portfolioId,
                AssetClass = AssetClass.Cash,
                ValuationMode = AssetValuationMode.Manual,
                Currency = "PLN",
                Quantity = 0,
                ManualValueAmount = 42m,
            });

        await RunConsumerAsync(positionsClient, new FakePriceQuoteClient(), async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            var messageId = Guid.NewGuid();
            var @event = new DailyPricesSynced
            {
                RunId = Guid.NewGuid(),
                SyncDate = snapshotDate,
                SyncedCount = 1,
                FailedCount = 0,
                NoDataCount = 0,
            };

            await bus.Publish(@event, ctx => ctx.MessageId = messageId, cancellationToken);
            await WaitForSnapshotAsync(provider, portfolioId, snapshotDate, cancellationToken);

            // Redeliver: same MessageId, same content. The inbox (InboxState, keyed on MessageId +
            // ConsumerId) must recognize it and skip the consumer body entirely.
            await bus.Publish(@event, ctx => ctx.MessageId = messageId, cancellationToken);

            // No signal to wait on for "it was skipped" — give a genuine redelivery time to land
            // before asserting it never did.
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            var snapshots = await db.ValuationSnapshots
                .IgnoreQueryFilters()
                .Where(s => s.PortfolioId == portfolioId && s.Date == snapshotDate)
                .ToListAsync(cancellationToken);

            var snapshot = Assert.Single(snapshots);
            Assert.Equal(42m, snapshot.TotalPln);
        }, cancellationToken);
    }

    private async Task RunConsumerAsync(
        IPositionsClient positionsClient,
        IPriceQuoteClient priceQuoteClient,
        Func<ServiceProvider, Task> action,
        CancellationToken cancellationToken)
    {
        await using var provider = BuildProvider(positionsClient, priceQuoteClient);
        var hostedServices = provider.GetServices<IHostedService>().ToList();

        foreach (var hostedService in hostedServices)
        {
            await hostedService.StartAsync(cancellationToken);
        }

        try
        {
            // See UserRegisteredIdempotentConsumerTests: a publish fired immediately after
            // StartAsync can race the exchange->queue binding on a fresh queue and be dropped.
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

    private static async Task<ValuationSnapshot> WaitForSnapshotAsync(
        ServiceProvider provider, Guid portfolioId, DateOnly date, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            var snapshot = await db.ValuationSnapshots
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(s => s.PortfolioId == portfolioId && s.Date == date, cancellationToken);

            if (snapshot is not null)
            {
                return snapshot;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }

        throw new TimeoutException($"No ValuationSnapshot for portfolio {portfolioId} on {date} within the deadline.");
    }

    private ServiceProvider BuildProvider(IPositionsClient positionsClient, IPriceQuoteClient priceQuoteClient)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(positionsClient);
        services.AddSingleton(priceQuoteClient);

        // No HTTP request in this bare provider (same as production: a MassTransit consumer has no
        // HttpContext) — the consumer never reads ICurrentUser.UserId (writes set UserId explicitly,
        // reads use IgnoreQueryFilters), but ReportingDbContext's constructor still needs some
        // implementation to satisfy DI. DesignTimeCurrentUser's UserId is Guid.Empty, not a throw.
        services.AddSingleton<ICurrentUser, DesignTimeCurrentUser>();

        services.AddDbContext<ReportingDbContext>(options => options.UseNpgsql(containers.PostgresConnectionString));

        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();

            // No UseBusOutbox() here: this test only exercises the consumer-side inbox
            // (UseEntityFrameworkOutbox on the receive endpoint, wired by
            // IdempotentConsumerDefinition), never IBus/IPublishEndpoint from a DI scope, so the
            // producer-side bus outbox and its background delivery poller would be dead weight.
            x.AddEntityFrameworkOutbox<ReportingDbContext>(o => o.UsePostgres());

            x.AddConsumer<DailyPricesSyncedConsumer>(typeof(TestConsumerDefinition));

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(new Uri(containers.RabbitMqConnectionString));

                cfg.ConfigureEndpoints(context);
            });
        });

        return services.BuildServiceProvider();
    }

    private sealed class TestConsumerDefinition : IdempotentConsumerDefinition<DailyPricesSyncedConsumer, ReportingDbContext>
    {
        public TestConsumerDefinition() => Endpoint(e => e.Name = QueueName);
    }
}
