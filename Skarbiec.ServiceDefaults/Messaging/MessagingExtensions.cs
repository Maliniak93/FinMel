using MassTransit;
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
