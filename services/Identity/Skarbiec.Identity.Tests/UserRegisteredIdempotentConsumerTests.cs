using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Skarbiec.Contracts.Events;
using Skarbiec.Identity.Data;
using Skarbiec.ServiceDefaults.Messaging;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Identity.Tests;

// Builds its own provider with a counting consumer on a queue unique to this class.
[Collection(TestingDefaults.CollectionName)]
public sealed class UserRegisteredIdempotentConsumerTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private const string QueueName = "user-registered-idempotency-test";

    public async ValueTask InitializeAsync()
    {
        await using var provider = BuildProvider(new ConsumeTracker());
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();

        await containers.ResetDatabaseAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Consume_SameMessageIdDeliveredTwice_HandlerRunsOnlyOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tracker = new ConsumeTracker();

        await using var provider = BuildProvider(tracker);
        var hostedServices = provider.GetServices<IHostedService>().ToList();

        foreach (var hostedService in hostedServices)
        {
            await hostedService.StartAsync(cancellationToken);
        }

        try
        {
            // StartAsync does not wait for RabbitMQ to apply the bindings, so a publish right after it could be dropped.
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);

            var bus = provider.GetRequiredService<IBus>();
            var messageId = Guid.NewGuid();
            var @event = new UserRegistered
            {
                UserId = Guid.NewGuid(),
                Email = $"{Guid.NewGuid()}@example.com",
                DisplayName = "Ada Lovelace",
                OccurredAtUtc = DateTimeOffset.UtcNow
            };

            await bus.Publish(@event, ctx => ctx.MessageId = messageId, cancellationToken);
            await tracker.FirstConsume.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);

            // Same MessageId: the inbox must skip the consumer body.
            await bus.Publish(@event, ctx => ctx.MessageId = messageId, cancellationToken);

            // Nothing signals a skip, so give a redelivery time to land before asserting it never did.
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            Assert.Equal(1, tracker.ConsumeCount);
        }
        finally
        {
            for (var i = hostedServices.Count - 1; i >= 0; i--)
            {
                await hostedServices[i].StopAsync(cancellationToken);
            }
        }
    }

    private ServiceProvider BuildProvider(ConsumeTracker tracker)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(tracker);

        services.AddDbContext<IdentityDbContext>(options => options.UseNpgsql(containers.PostgresConnectionString));

        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();

            // No UseBusOutbox(): only the consumer-side inbox is under test.
            x.AddEntityFrameworkOutbox<IdentityDbContext>(o => o.UsePostgres());

            x.AddConsumer<CountingConsumer>(typeof(CountingConsumerDefinition));

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(new Uri(containers.RabbitMqConnectionString));

                cfg.ConfigureEndpoints(context);
            });
        });

        return services.BuildServiceProvider();
    }

    private sealed class ConsumeTracker
    {
        private int _consumeCount;

        public TaskCompletionSource<bool> FirstConsume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ConsumeCount => _consumeCount;

        public void RecordConsume()
        {
            Interlocked.Increment(ref _consumeCount);
            FirstConsume.TrySetResult(true);
        }
    }

    private sealed class CountingConsumer(ConsumeTracker tracker) : IConsumer<UserRegistered>
    {
        public Task Consume(ConsumeContext<UserRegistered> context)
        {
            tracker.RecordConsume();
            return Task.CompletedTask;
        }
    }

    private sealed class CountingConsumerDefinition : IdempotentConsumerDefinition<CountingConsumer, IdentityDbContext>
    {
        public CountingConsumerDefinition() => Endpoint(e => e.Name = QueueName);
    }
}
