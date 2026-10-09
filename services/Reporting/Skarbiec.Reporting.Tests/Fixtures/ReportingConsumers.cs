using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.MarketData;
using Skarbiec.Reporting.Messaging;
using Skarbiec.Reporting.Valuation;
using Skarbiec.ServiceDefaults.Authentication;
using Skarbiec.ServiceDefaults.Tenancy;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Reporting.Tests.Fixtures;

// Each test class registers its consumers under its own queue name: queues are durable and outlive one class on the shared broker.
internal static class ReportingConsumers
{
    public static DateOnly Today => DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime);

    public static ServiceProvider BuildProvider(
        SkarbiecContainersFixture containers,
        Action<IBusRegistrationConfigurator> configureConsumers,
        IPriceQuoteClient? priceQuoteClient = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(priceQuoteClient ?? new FakePriceQuoteClient());
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<PortfolioSnapshotWriter>();

        // Consumers never read ICurrentUser.UserId, but ReportingDbContext's constructor needs an implementation.
        services.AddSingleton<ICurrentUser, DesignTimeCurrentUser>();

        services.AddDbContext<ReportingDbContext>(options => options.UseNpgsql(containers.PostgresConnectionString));

        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();

            // No UseBusOutbox(): only the consumer-side inbox is under test.
            x.AddEntityFrameworkOutbox<ReportingDbContext>(o => o.UsePostgres());

            configureConsumers(x);

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(new Uri(containers.RabbitMqConnectionString));

                cfg.ConfigureEndpoints(context);
            });
        });

        return services.BuildServiceProvider();
    }

    public static async Task MigrateAndResetAsync(SkarbiecContainersFixture containers)
    {
        await using (var db = OpenDbContext(containers))
        {
            await db.Database.MigrateAsync();
        }

        await containers.ResetDatabaseAsync();
    }

    public static ReportingDbContext OpenDbContext(SkarbiecContainersFixture containers)
    {
        var options = new DbContextOptionsBuilder<ReportingDbContext>()
            .UseNpgsql(containers.PostgresConnectionString)
            .Options;

        return new ReportingDbContext(options, new DesignTimeCurrentUser());
    }

    public static async Task RunAsync(
        SkarbiecContainersFixture containers,
        Action<IBusRegistrationConfigurator> configureConsumers,
        Func<ServiceProvider, Task> action,
        CancellationToken cancellationToken,
        IPriceQuoteClient? priceQuoteClient = null)
    {
        await using var provider = BuildProvider(containers, configureConsumers, priceQuoteClient);
        var hostedServices = provider.GetServices<IHostedService>().ToList();

        foreach (var hostedService in hostedServices)
        {
            await hostedService.StartAsync(cancellationToken);
        }

        try
        {
            // StartAsync does not wait for RabbitMQ to apply the bindings, so a publish right after it could be dropped.
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

    public static async Task<T> WaitForAsync<T>(
        IServiceProvider provider,
        Func<ReportingDbContext, CancellationToken, Task<T>> probe,
        Func<T, bool> predicate,
        string description,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            var value = await probe(db, cancellationToken);

            if (predicate(value))
            {
                return value;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }

        throw new TimeoutException($"{description} never happened within the deadline.");
    }

    public static async Task<Position> WaitForPositionAsync(
        IServiceProvider provider, Guid assetId, CancellationToken cancellationToken, Func<Position, bool>? predicate = null)
    {
        predicate ??= _ => true;
        var position = await WaitForAsync(
            provider,
            (db, ct) => db.Positions.IgnoreQueryFilters().SingleOrDefaultAsync(p => p.AssetId == assetId, ct),
            p => p is not null && predicate(p),
            $"A matching Position for asset {assetId}",
            cancellationToken);

        return position!;
    }

    public static Task WaitForPositionGoneAsync(IServiceProvider provider, Guid assetId, CancellationToken cancellationToken) =>
        WaitForAsync(
            provider,
            (db, ct) => db.Positions.IgnoreQueryFilters().AnyAsync(p => p.AssetId == assetId, ct),
            stillExists => !stillExists,
            $"Deleting the Position for asset {assetId}",
            cancellationToken);

    public static Task<List<Position>> WaitForPositionsAsync(
        IServiceProvider provider, Guid portfolioId, CancellationToken cancellationToken, Func<List<Position>, bool> predicate) =>
        WaitForAsync(
            provider,
            (db, ct) => db.Positions.IgnoreQueryFilters().Where(p => p.PortfolioId == portfolioId).ToListAsync(ct),
            positions => positions.Count > 0 && predicate(positions),
            $"The expected state of the Positions for portfolio {portfolioId}",
            cancellationToken);

    public static Task WaitForNoPositionsAsync(IServiceProvider provider, Guid portfolioId, CancellationToken cancellationToken) =>
        WaitForAsync(
            provider,
            (db, ct) => db.Positions.IgnoreQueryFilters().AnyAsync(p => p.PortfolioId == portfolioId, ct),
            stillExists => !stillExists,
            $"Deleting the Positions for portfolio {portfolioId}",
            cancellationToken);

    public static async Task<ValuationSnapshot> WaitForSnapshotAsync(
        IServiceProvider provider,
        Guid portfolioId,
        DateOnly date,
        CancellationToken cancellationToken,
        Func<ValuationSnapshot, bool>? predicate = null)
    {
        predicate ??= _ => true;
        var snapshot = await WaitForAsync(
            provider,
            (db, ct) => db.ValuationSnapshots.IgnoreQueryFilters()
                .SingleOrDefaultAsync(s => s.PortfolioId == portfolioId && s.Date == date, ct),
            s => s is not null && predicate(s),
            $"A matching ValuationSnapshot for portfolio {portfolioId} on {date}",
            cancellationToken);

        return snapshot!;
    }

    public static async Task<List<AssetValuation>> GetLinesAsync(
        SkarbiecContainersFixture containers, Guid portfolioId, DateOnly date, CancellationToken cancellationToken)
    {
        await using var db = OpenDbContext(containers);
        return await db.AssetValuations.IgnoreQueryFilters()
            .Where(l => l.PortfolioId == portfolioId && l.Date == date)
            .ToListAsync(cancellationToken);
    }

    public static async Task<ValuationSnapshot?> GetSnapshotAsync(
        SkarbiecContainersFixture containers, Guid portfolioId, DateOnly date, CancellationToken cancellationToken)
    {
        await using var db = OpenDbContext(containers);
        return await db.ValuationSnapshots.IgnoreQueryFilters()
            .SingleOrDefaultAsync(s => s.PortfolioId == portfolioId && s.Date == date, cancellationToken);
    }

    public static async Task<HistoryRebuildRequest?> GetRebuildRequestAsync(
        SkarbiecContainersFixture containers, Guid portfolioId, CancellationToken cancellationToken)
    {
        await using var db = OpenDbContext(containers);
        return await db.HistoryRebuildRequests.IgnoreQueryFilters()
            .SingleOrDefaultAsync(r => r.PortfolioId == portfolioId, cancellationToken);
    }

    public static async Task<HistoryRebuildRequest> WaitForRebuildRequestAsync(
        IServiceProvider provider, Guid portfolioId, CancellationToken cancellationToken, Func<HistoryRebuildRequest, bool>? predicate = null)
    {
        predicate ??= _ => true;
        var request = await WaitForAsync(
            provider,
            (db, ct) => db.HistoryRebuildRequests.IgnoreQueryFilters().SingleOrDefaultAsync(r => r.PortfolioId == portfolioId, ct),
            r => r is not null && predicate(r),
            $"A matching HistoryRebuildRequest for portfolio {portfolioId}",
            cancellationToken);

        return request!;
    }

    public static Task WaitForNoRebuildRequestAsync(IServiceProvider provider, Guid portfolioId, CancellationToken cancellationToken) =>
        WaitForAsync(
            provider,
            (db, ct) => db.HistoryRebuildRequests.IgnoreQueryFilters().AnyAsync(r => r.PortfolioId == portfolioId, ct),
            stillExists => !stillExists,
            $"Deleting the HistoryRebuildRequest for portfolio {portfolioId}",
            cancellationToken);

    public static async Task<List<AssetValuation>> GetAllLinesAsync(
        SkarbiecContainersFixture containers, Guid portfolioId, CancellationToken cancellationToken)
    {
        await using var db = OpenDbContext(containers);
        return await db.AssetValuations.IgnoreQueryFilters()
            .Where(l => l.PortfolioId == portfolioId)
            .ToListAsync(cancellationToken);
    }

    public static async Task<List<ValuationSnapshot>> GetAllSnapshotsAsync(
        SkarbiecContainersFixture containers, Guid portfolioId, CancellationToken cancellationToken)
    {
        await using var db = OpenDbContext(containers);
        return await db.ValuationSnapshots.IgnoreQueryFilters()
            .Where(s => s.PortfolioId == portfolioId)
            .ToListAsync(cancellationToken);
    }

    public static Task PublishRebuildRequestedAsync(
        this IBus bus, Guid portfolioId, Guid userId, CancellationToken cancellationToken, Guid? messageId = null) =>
        bus.Publish(
            new PortfolioHistoryRebuildRequested { PortfolioId = portfolioId, UserId = userId },
            context =>
            {
                if (messageId is { } id)
                {
                    context.MessageId = id;
                }
            },
            cancellationToken);
}
