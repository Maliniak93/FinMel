using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts.Events;

namespace Skarbiec.Identity.Tests.Messaging;

/// <summary>
/// A standalone MassTransit bus, independent of the Identity service under test, subscribing to
/// <see cref="UserRegistered"/> on the shared Testcontainers RabbitMQ broker — proves fan-out
/// delivery to a real consumer over the real transport (T0.10 AC), as opposed to the outbox-write
/// guarantee (<c>UserRegisteredOutboxTests</c>), which never starts a bus at all.
/// </summary>
public sealed class TestConsumerHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IBusControl _busControl;

    public TaskCompletionSource<UserRegistered> Received { get; }

    private TestConsumerHost(ServiceProvider provider, IBusControl busControl, TaskCompletionSource<UserRegistered> received)
    {
        _provider = provider;
        _busControl = busControl;
        Received = received;
    }

    public static async Task<TestConsumerHost> StartAsync(string rabbitMqConnectionString, CancellationToken cancellationToken)
    {
        var received = new TaskCompletionSource<UserRegistered>(TaskCreationOptions.RunContinuationsAsynchronously);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(received);

        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();
            x.AddConsumer<UserRegisteredTestConsumer>();

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(new Uri(rabbitMqConnectionString));

                cfg.ConfigureEndpoints(context);
            });
        });

        var provider = services.BuildServiceProvider();
        var busControl = provider.GetRequiredService<IBusControl>();
        await busControl.StartAsync(cancellationToken);

        return new TestConsumerHost(provider, busControl, received);
    }

    public async ValueTask DisposeAsync()
    {
        await _busControl.StopAsync();
        await _provider.DisposeAsync();
    }
}
