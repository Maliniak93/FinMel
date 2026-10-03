using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Testing.Messaging;

// Never starts the bus or the delivery poller unless asked, so an outbox row stays readable for the assertion.
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
