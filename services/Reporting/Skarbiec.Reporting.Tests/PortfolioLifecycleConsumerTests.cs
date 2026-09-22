using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Messaging;
using Skarbiec.Reporting.Tests.Fixtures;
using Skarbiec.ServiceDefaults.Authentication;
using Skarbiec.ServiceDefaults.Messaging;
using Skarbiec.ServiceDefaults.Tenancy;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Reporting.Tests;

/// <summary>
/// <c>PortfolioArchived</c>/<c>PortfolioRestored</c>/<c>PortfolioDeleted</c> in — spec-03 AC6-7.
/// Archive/restore flips every <see cref="Position.PortfolioIsArchived"/> flag for the portfolio
/// (belt and braces next to spec-02's per-asset <c>AssetPositionChanged</c> fan-out). Delete removes
/// that portfolio's <see cref="Position"/>, <see cref="AssetValuation"/> and <see cref="ValuationSnapshot"/>
/// rows entirely (design decision 1: a deleted portfolio must not keep a ghost value in net worth)
/// without touching any other portfolio. All three consumers share one provider/bus on queue names
/// unique to this test class, mirroring <c>AssetPositionChangedConsumerTests</c>.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class PortfolioLifecycleConsumerTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private const string ArchivedQueueName = "portfolio-archived-consumer-test";
    private const string RestoredQueueName = "portfolio-restored-consumer-test";
    private const string DeletedQueueName = "portfolio-deleted-consumer-test";

    public async ValueTask InitializeAsync()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReportingDbContext>().Database.MigrateAsync();

        await containers.ResetDatabaseAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Consume_PortfolioArchivedThenRestored_FlipsFlagOnEveryPosition()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var portfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var assetOneId = Guid.NewGuid();
        var assetTwoId = Guid.NewGuid();

        await using (var seedProvider = BuildProvider())
        {
            await using var scope = seedProvider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();

            await db.SeedPositionAsync(assetOneId, userId, portfolioId, cancellationToken, portfolioIsArchived: false);
            await db.SeedPositionAsync(assetTwoId, userId, portfolioId, cancellationToken, portfolioIsArchived: false);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();

            await bus.Publish(new PortfolioArchived
            {
                PortfolioId = portfolioId,
                UserId = userId,
                OccurredAtUtc = DateTimeOffset.UtcNow,
            }, cancellationToken);

            await WaitForPositionsAsync(provider, portfolioId, cancellationToken, positions => positions.All(p => p.PortfolioIsArchived));

            await bus.Publish(new PortfolioRestored
            {
                PortfolioId = portfolioId,
                UserId = userId,
                OccurredAtUtc = DateTimeOffset.UtcNow,
            }, cancellationToken);

            var restored = await WaitForPositionsAsync(
                provider, portfolioId, cancellationToken, positions => positions.All(p => !p.PortfolioIsArchived));

            Assert.Equal(2, restored.Count);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_PortfolioDeleted_RemovesPositionsAndValuationHistory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var deletedPortfolioId = Guid.NewGuid();
        var otherPortfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var deletedAssetId = Guid.NewGuid();
        var otherAssetId = Guid.NewGuid();
        var valuationDate = new DateOnly(2026, 8, 1);

        await using (var seedProvider = BuildProvider())
        {
            await using var scope = seedProvider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();

            await db.SeedPositionAsync(deletedAssetId, userId, deletedPortfolioId, cancellationToken);
            await db.SeedValuationLineAsync(userId, deletedPortfolioId, deletedAssetId, valuationDate, 1_000m, cancellationToken);
            await db.SeedSnapshotAsync(userId, deletedPortfolioId, valuationDate, 1_000m, cancellationToken);

            await db.SeedPositionAsync(otherAssetId, userId, otherPortfolioId, cancellationToken);
            await db.SeedValuationLineAsync(userId, otherPortfolioId, otherAssetId, valuationDate, 2_000m, cancellationToken);
            await db.SeedSnapshotAsync(userId, otherPortfolioId, valuationDate, 2_000m, cancellationToken);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(new PortfolioDeleted
            {
                PortfolioId = deletedPortfolioId,
                UserId = userId,
                OccurredAtUtc = DateTimeOffset.UtcNow,
            }, cancellationToken);

            await WaitForNoPositionsAsync(provider, deletedPortfolioId, cancellationToken);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();

            Assert.Empty(await db.Positions.IgnoreQueryFilters().Where(p => p.PortfolioId == deletedPortfolioId).ToListAsync(cancellationToken));
            Assert.Empty(await db.AssetValuations.IgnoreQueryFilters().Where(l => l.PortfolioId == deletedPortfolioId).ToListAsync(cancellationToken));
            Assert.Empty(await db.ValuationSnapshots.IgnoreQueryFilters().Where(s => s.PortfolioId == deletedPortfolioId).ToListAsync(cancellationToken));

            Assert.Single(await db.Positions.IgnoreQueryFilters().Where(p => p.PortfolioId == otherPortfolioId).ToListAsync(cancellationToken));
            Assert.Single(await db.AssetValuations.IgnoreQueryFilters().Where(l => l.PortfolioId == otherPortfolioId).ToListAsync(cancellationToken));
            Assert.Single(await db.ValuationSnapshots.IgnoreQueryFilters().Where(s => s.PortfolioId == otherPortfolioId).ToListAsync(cancellationToken));
        }, cancellationToken);
    }

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
            // See AssetPositionChangedConsumerTests: a publish fired immediately after StartAsync can
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

    private static async Task<List<Position>> WaitForPositionsAsync(
        ServiceProvider provider, Guid portfolioId, CancellationToken cancellationToken, Func<List<Position>, bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            var positions = await db.Positions.IgnoreQueryFilters()
                .Where(p => p.PortfolioId == portfolioId)
                .ToListAsync(cancellationToken);

            if (positions.Count > 0 && predicate(positions))
            {
                return positions;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }

        throw new TimeoutException($"Positions for portfolio {portfolioId} never matched the expected state within the deadline.");
    }

    private static async Task WaitForNoPositionsAsync(ServiceProvider provider, Guid portfolioId, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            var stillExists = await db.Positions.IgnoreQueryFilters().AnyAsync(p => p.PortfolioId == portfolioId, cancellationToken);

            if (!stillExists)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }

        throw new TimeoutException($"Positions for portfolio {portfolioId} were not deleted within the deadline.");
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentUser, DesignTimeCurrentUser>();

        services.AddDbContext<ReportingDbContext>(options => options.UseNpgsql(containers.PostgresConnectionString));

        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();
            x.AddEntityFrameworkOutbox<ReportingDbContext>(o => o.UsePostgres());

            x.AddConsumer<PortfolioArchivedConsumer>(typeof(ArchivedConsumerDefinition));
            x.AddConsumer<PortfolioRestoredConsumer>(typeof(RestoredConsumerDefinition));
            x.AddConsumer<PortfolioDeletedConsumer>(typeof(DeletedConsumerDefinition));

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(new Uri(containers.RabbitMqConnectionString));

                cfg.ConfigureEndpoints(context);
            });
        });

        return services.BuildServiceProvider();
    }

    private sealed class ArchivedConsumerDefinition : IdempotentConsumerDefinition<PortfolioArchivedConsumer, ReportingDbContext>
    {
        public ArchivedConsumerDefinition() => Endpoint(e => e.Name = ArchivedQueueName);
    }

    private sealed class RestoredConsumerDefinition : IdempotentConsumerDefinition<PortfolioRestoredConsumer, ReportingDbContext>
    {
        public RestoredConsumerDefinition() => Endpoint(e => e.Name = RestoredQueueName);
    }

    private sealed class DeletedConsumerDefinition : IdempotentConsumerDefinition<PortfolioDeletedConsumer, ReportingDbContext>
    {
        public DeletedConsumerDefinition() => Endpoint(e => e.Name = DeletedQueueName);
    }
}
