using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.MarketData;
using Skarbiec.Reporting.Valuation;
using Skarbiec.ServiceDefaults.Authentication;
using Skarbiec.ServiceDefaults.Tenancy;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Reporting.Tests.Fixtures;

/// <summary>
/// Bare-provider harness for Reporting's consumer tests: no HTTP host (a consumer only touches
/// <see cref="ReportingDbContext"/>), a live RabbitMQ bus, and the same service registrations the
/// consumers get from <c>Program.cs</c>. Each test class registers its own consumer(s) under a queue
/// name unique to that class — queues are durable and outlive one class on the shared broker.
/// </summary>
/// <remarks>
/// spec-07: <c>AssetPositionChangedConsumer</c>, <c>AssetRemovedConsumer</c>,
/// <c>PortfolioRestoredConsumer</c> and <c>DailyPricesSyncedConsumer</c> all revalue through the
/// shared <see cref="PortfolioSnapshotWriter"/> and resolve "today" from <see cref="TimeProvider"/>,
/// so every provider built here registers both — one place instead of four drifting copies.
/// </remarks>
internal static class ReportingConsumers
{
    /// <summary>Today as the event path sees it: the UTC date, matching <c>DailyPricesSynced.SyncDate</c> (spec-07 design decision 2).</summary>
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

        // No HTTP request in this bare provider (same as production: a MassTransit consumer has no
        // HttpContext) — consumers never read ICurrentUser.UserId (writes set UserId explicitly from
        // the event, reads use IgnoreQueryFilters), but ReportingDbContext's constructor still needs
        // some implementation to satisfy DI. DesignTimeCurrentUser's UserId is Guid.Empty, not a throw.
        services.AddSingleton<ICurrentUser, DesignTimeCurrentUser>();

        services.AddDbContext<ReportingDbContext>(options => options.UseNpgsql(containers.PostgresConnectionString));

        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();

            // No UseBusOutbox() here: these tests only exercise the consumer-side inbox, never
            // IBus/IPublishEndpoint from a DI scope, so the producer-side bus outbox and its
            // background delivery poller would be dead weight.
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

    /// <summary>Applies Reporting's migrations and wipes the shared database — call from a consumer test class's <c>InitializeAsync</c>.</summary>
    public static async Task MigrateAndResetAsync(SkarbiecContainersFixture containers)
    {
        await using (var db = OpenDbContext(containers))
        {
            await db.Database.MigrateAsync();
        }

        await containers.ResetDatabaseAsync();
    }

    /// <summary>A plain <see cref="ReportingDbContext"/> for arranging rows and reading results — callers use <c>IgnoreQueryFilters</c> to read across users.</summary>
    public static ReportingDbContext OpenDbContext(SkarbiecContainersFixture containers)
    {
        var options = new DbContextOptionsBuilder<ReportingDbContext>()
            .UseNpgsql(containers.PostgresConnectionString)
            .Options;

        return new ReportingDbContext(options, new DesignTimeCurrentUser());
    }

    /// <summary>Builds a provider, starts its bus, runs <paramref name="action"/> and stops the bus again.</summary>
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
            // A publish fired immediately after StartAsync can race the exchange->queue binding on
            // a fresh queue and be dropped.
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

    /// <summary>
    /// Polls <paramref name="probe"/> (each attempt on a fresh DbContext) until it yields a value
    /// satisfying <paramref name="predicate"/>, or throws <see cref="TimeoutException"/> after 15 s.
    /// </summary>
    public static async Task<T> WaitForAsync<T>(
        ServiceProvider provider,
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
        ServiceProvider provider, Guid assetId, CancellationToken cancellationToken, Func<Position, bool>? predicate = null)
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

    public static Task WaitForPositionGoneAsync(ServiceProvider provider, Guid assetId, CancellationToken cancellationToken) =>
        WaitForAsync(
            provider,
            (db, ct) => db.Positions.IgnoreQueryFilters().AnyAsync(p => p.AssetId == assetId, ct),
            stillExists => !stillExists,
            $"Deleting the Position for asset {assetId}",
            cancellationToken);

    public static Task<List<Position>> WaitForPositionsAsync(
        ServiceProvider provider, Guid portfolioId, CancellationToken cancellationToken, Func<List<Position>, bool> predicate) =>
        WaitForAsync(
            provider,
            (db, ct) => db.Positions.IgnoreQueryFilters().Where(p => p.PortfolioId == portfolioId).ToListAsync(ct),
            positions => positions.Count > 0 && predicate(positions),
            $"The expected state of the Positions for portfolio {portfolioId}",
            cancellationToken);

    public static Task WaitForNoPositionsAsync(ServiceProvider provider, Guid portfolioId, CancellationToken cancellationToken) =>
        WaitForAsync(
            provider,
            (db, ct) => db.Positions.IgnoreQueryFilters().AnyAsync(p => p.PortfolioId == portfolioId, ct),
            stillExists => !stillExists,
            $"Deleting the Positions for portfolio {portfolioId}",
            cancellationToken);

    public static async Task<ValuationSnapshot> WaitForSnapshotAsync(
        ServiceProvider provider,
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

    /// <summary>Every <see cref="AssetValuation"/> line of <paramref name="portfolioId"/> on <paramref name="date"/>, across users.</summary>
    public static async Task<List<AssetValuation>> GetLinesAsync(
        SkarbiecContainersFixture containers, Guid portfolioId, DateOnly date, CancellationToken cancellationToken)
    {
        await using var db = OpenDbContext(containers);
        return await db.AssetValuations.IgnoreQueryFilters()
            .Where(l => l.PortfolioId == portfolioId && l.Date == date)
            .ToListAsync(cancellationToken);
    }

    /// <summary>The <see cref="ValuationSnapshot"/> of <paramref name="portfolioId"/> on <paramref name="date"/>, or <c>null</c>.</summary>
    public static async Task<ValuationSnapshot?> GetSnapshotAsync(
        SkarbiecContainersFixture containers, Guid portfolioId, DateOnly date, CancellationToken cancellationToken)
    {
        await using var db = OpenDbContext(containers);
        return await db.ValuationSnapshots.IgnoreQueryFilters()
            .SingleOrDefaultAsync(s => s.PortfolioId == portfolioId && s.Date == date, cancellationToken);
    }
}
