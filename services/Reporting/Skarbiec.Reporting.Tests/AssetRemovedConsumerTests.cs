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
/// <c>AssetRemoved</c> in, <see cref="Position"/> gone but its historical
/// <see cref="AssetValuation"/> lines kept — spec-03 AC5, design decision 2 ("AssetRemoved keeps the
/// lines": deleting an asset must not silently change what last month's net worth was). Builds its
/// own provider on a queue name unique to this test class, mirroring
/// <c>AssetPositionChangedConsumerTests</c>.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class AssetRemovedConsumerTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private const string QueueName = "asset-removed-consumer-test";

    public async ValueTask InitializeAsync()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReportingDbContext>().Database.MigrateAsync();

        await containers.ResetDatabaseAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Consume_AssetRemoved_DeletesPositionAndKeepsValuationHistory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var assetId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var valuationDate = new DateOnly(2026, 8, 1);

        await using (var seedProvider = BuildProvider())
        {
            await using var scope = seedProvider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();

            await db.SeedPositionAsync(assetId, userId, portfolioId, cancellationToken);
            await db.SeedValuationLineAsync(userId, portfolioId, assetId, valuationDate, 1_000m, cancellationToken);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(new AssetRemoved
            {
                AssetId = assetId,
                PortfolioId = portfolioId,
                UserId = userId,
                OccurredAtUtc = DateTimeOffset.UtcNow,
            }, cancellationToken);

            await WaitForPositionGoneAsync(provider, assetId, cancellationToken);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            var lines = await db.AssetValuations.IgnoreQueryFilters()
                .Where(l => l.AssetId == assetId)
                .ToListAsync(cancellationToken);

            Assert.Single(lines);
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

    private static async Task WaitForPositionGoneAsync(ServiceProvider provider, Guid assetId, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            var stillExists = await db.Positions.IgnoreQueryFilters().AnyAsync(p => p.AssetId == assetId, cancellationToken);

            if (!stillExists)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }

        throw new TimeoutException($"Position for asset {assetId} was not deleted within the deadline.");
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
            x.AddConsumer<AssetRemovedConsumer>(typeof(TestConsumerDefinition));

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(new Uri(containers.RabbitMqConnectionString));

                cfg.ConfigureEndpoints(context);
            });
        });

        return services.BuildServiceProvider();
    }

    private sealed class TestConsumerDefinition : IdempotentConsumerDefinition<AssetRemovedConsumer, ReportingDbContext>
    {
        public TestConsumerDefinition() => Endpoint(e => e.Name = QueueName);
    }
}
