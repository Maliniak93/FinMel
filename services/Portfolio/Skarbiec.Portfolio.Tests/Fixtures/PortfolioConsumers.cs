using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Skarbiec.Portfolio.Data;
using Skarbiec.ServiceDefaults.Authentication;
using Skarbiec.ServiceDefaults.Tenancy;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests.Fixtures;

// Each test class registers its consumers under its own queue name: queues are durable and outlive one class on the shared broker.
internal static class PortfolioConsumers
{
    public static ServiceProvider BuildProvider(
        SkarbiecContainersFixture containers, Action<IBusRegistrationConfigurator> configureConsumers)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);

        // Consumers never read ICurrentUser.UserId, but PortfolioDbContext's constructor needs an implementation.
        services.AddSingleton<ICurrentUser, DesignTimeCurrentUser>();

        services.AddDbContext<PortfolioDbContext>(options => options.UseNpgsql(containers.PostgresConnectionString));

        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();

            // No UseBusOutbox(): only the consumer-side inbox is under test.
            x.AddEntityFrameworkOutbox<PortfolioDbContext>(o => o.UsePostgres());

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

    public static PortfolioDbContext OpenDbContext(SkarbiecContainersFixture containers, Guid? userId = null)
    {
        var options = new DbContextOptionsBuilder<PortfolioDbContext>()
            .UseNpgsql(containers.PostgresConnectionString)
            .Options;

        ICurrentUser currentUser = userId is { } id ? new StubCurrentUser(id) : new DesignTimeCurrentUser();

        return new PortfolioDbContext(options, currentUser);
    }

    public static async Task RunAsync(
        SkarbiecContainersFixture containers,
        Action<IBusRegistrationConfigurator> configureConsumers,
        Func<ServiceProvider, Task> action,
        CancellationToken cancellationToken)
    {
        await using var provider = BuildProvider(containers, configureConsumers);
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
        Func<PortfolioDbContext, CancellationToken, Task<T>> probe,
        Func<T, bool> predicate,
        string description,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
            var value = await probe(db, cancellationToken);

            if (predicate(value))
            {
                return value;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }

        throw new TimeoutException($"{description} never happened within the deadline.");
    }

    public static async Task<TargetAllocationReplica> WaitForTargetReplicaAsync(
        IServiceProvider provider,
        Guid userId,
        CancellationToken cancellationToken,
        Func<TargetAllocationReplica, bool>? predicate = null)
    {
        predicate ??= _ => true;
        var replica = await WaitForAsync(
            provider,
            (db, ct) => db.Set<TargetAllocationReplica>().IgnoreQueryFilters().SingleOrDefaultAsync(r => r.UserId == userId, ct),
            r => r is not null && predicate(r),
            $"A matching TargetAllocationReplica for user {userId}",
            cancellationToken);

        return replica!;
    }

    public static async Task<ValuationReplica> WaitForValuationReplicaAsync(
        IServiceProvider provider,
        Guid userId,
        CancellationToken cancellationToken,
        Func<ValuationReplica, bool>? predicate = null)
    {
        predicate ??= _ => true;
        var replica = await WaitForAsync(
            provider,
            (db, ct) => db.Set<ValuationReplica>().IgnoreQueryFilters().SingleOrDefaultAsync(r => r.UserId == userId, ct),
            r => r is not null && predicate(r),
            $"A matching ValuationReplica for user {userId}",
            cancellationToken);

        return replica!;
    }

    public static async Task<TargetAllocationReplica?> GetTargetReplicaAsync(
        SkarbiecContainersFixture containers, Guid userId, CancellationToken cancellationToken)
    {
        await using var db = OpenDbContext(containers);
        return await db.Set<TargetAllocationReplica>().IgnoreQueryFilters()
            .SingleOrDefaultAsync(r => r.UserId == userId, cancellationToken);
    }

    public static async Task<ValuationReplica?> GetValuationReplicaAsync(
        SkarbiecContainersFixture containers, Guid userId, CancellationToken cancellationToken)
    {
        await using var db = OpenDbContext(containers);
        return await db.Set<ValuationReplica>().IgnoreQueryFilters()
            .SingleOrDefaultAsync(r => r.UserId == userId, cancellationToken);
    }
}
