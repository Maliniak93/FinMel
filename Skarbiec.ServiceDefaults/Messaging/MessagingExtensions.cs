using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Skarbiec.ServiceDefaults.Messaging;

/// <summary>
/// Wires MassTransit v8 with the RabbitMQ transport and the EF Core transactional outbox for
/// <typeparamref name="TDbContext"/> (T0.10, ADR-012). Call once per service, alongside
/// <c>AddServiceDefaults()</c> — the outbox's <typeparamref name="TDbContext"/> is service-specific
/// so this can't fold into the generic call.
/// </summary>
public static class MessagingExtensions
{
    /// <summary>
    /// Publish only through the outbox: inject <see cref="IPublishEndpoint"/> and call
    /// <c>Publish</c> inside the same DB transaction/<c>SaveChangesAsync</c> as the business
    /// write — never resolve <see cref="IBus"/> to send directly. Endpoint names are kebab-case,
    /// one queue per consumer (<c>SetKebabCaseEndpointNameFormatter</c> + <c>ConfigureEndpoints</c>).
    /// </summary>
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

/// <summary>
/// Reusable inbox template (T0.12, ADR-012): capped exponential retry, then
/// <see cref="EntityFrameworkOutboxConfigurationExtensions.UseEntityFrameworkOutbox{TDbContext}"/>
/// on the consumer's own receive endpoint, which deduplicates redelivered messages by
/// <c>MessageId</c> against the <c>InboxState</c> table (already added to every outbox-enabled
/// <c>DbContext</c> via <c>AddInboxStateEntity()</c>) before the consumer body ever runs a second
/// time. Copy into a new service by declaring an empty subclass and registering it alongside the
/// consumer: <c>x.AddConsumer&lt;MyConsumer&gt;(typeof(MyConsumerDefinition))</c> (the type-argument
/// form <c>AddConsumer&lt;TConsumer, TDefinition&gt;()</c> looks tempting but doesn't compile here —
/// <see cref="IRegistrationConfigurator"/> declares its own arity-1 generic <c>AddConsumer&lt;T&gt;</c>,
/// which hides the two-generic-argument extension method entirely) — see <c>Messaging/README.md</c>
/// next to this file for the full checklist.
/// </summary>
public abstract class IdempotentConsumerDefinition<TConsumer, TDbContext> : ConsumerDefinition<TConsumer>
    where TConsumer : class, IConsumer
    where TDbContext : DbContext
{
    /// <summary>
    /// Retry policy applied before a message is left to fault to the endpoint's error queue.
    /// Override for a faster policy in tests; production consumers should keep the capped
    /// exponential default so a slow dependency gets a few spaced-out attempts before giving up.
    /// </summary>
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
