using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Testing.Messaging;

/// <summary>
/// Builds a bare <see cref="ServiceProvider"/> wired with MassTransit's EF outbox (T0.10, ADR-012)
/// against <typeparamref name="TDbContext"/>, without ever starting its <see cref="IHostedService"/>s
/// (the bus, the outbox delivery poller) unless the caller does so explicitly. Used by every
/// service's "outbox row commits atomically with the business row" test and the durability test
/// proving survival across a process "restart" (T0.11) — a real hosted bus would race its own
/// delivery poller against the assertion, delivering/removing the row before the test can see it.
/// </summary>
public static class HostlessOutboxProvider
{
    public static ServiceProvider Build<TDbContext>(
        SkarbiecContainersFixture containers,
        Action<IServiceCollection> configureServices,
        Action<IBusRegistrationConfigurator>? configureConsumers = null)
        where TDbContext : DbContext
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDbContext<TDbContext>(options => options.UseNpgsql(containers.PostgresConnectionString));

        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();

            x.AddEntityFrameworkOutbox<TDbContext>(o =>
            {
                o.UsePostgres();
                o.UseBusOutbox();
            });

            configureConsumers?.Invoke(x);

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(new Uri(containers.RabbitMqConnectionString));

                cfg.ConfigureEndpoints(context);
            });
        });

        configureServices(services);

        return services.BuildServiceProvider();
    }
}
