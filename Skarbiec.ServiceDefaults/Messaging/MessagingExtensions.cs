using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Skarbiec.ServiceDefaults.Messaging;

public static class MessagingExtensions
{
    // Publish only through the outbox: IPublishEndpoint inside the business write's SaveChangesAsync, never IBus.
    public static TBuilder AddRabbitMqMessaging<TBuilder, TDbContext>(
        this TBuilder builder,
        string connectionStringName = "rabbitmq",
        Action<IBusRegistrationConfigurator>? configureConsumers = null)
        where TBuilder : IHostApplicationBuilder
        where TDbContext : DbContext
    {
        var connectionString = builder.Configuration.GetConnectionString(connectionStringName)
            ?? throw new InvalidOperationException($"Missing connection string '{connectionStringName}'.");

        builder.Services.AddMassTransit(x =>
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
                cfg.Host(new Uri(connectionString));

                cfg.ConfigureEndpoints(context);
            });
        });

        return builder;
    }
}

// Copy by declaring an empty subclass, registered as x.AddConsumer<MyConsumer>(typeof(MyConsumerDefinition)).
public abstract class IdempotentConsumerDefinition<TConsumer, TDbContext> : ConsumerDefinition<TConsumer>
    where TConsumer : class, IConsumer
    where TDbContext : DbContext
{
    // Override for a faster policy in tests; production keeps the capped exponential default.
    protected virtual Action<IRetryConfigurator> RetryPolicy => r =>
        r.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));

    protected sealed override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<TConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(RetryPolicy);
        endpointConfigurator.UseEntityFrameworkOutbox<TDbContext>(context);
    }
}
