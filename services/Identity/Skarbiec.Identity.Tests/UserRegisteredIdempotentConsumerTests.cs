using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Skarbiec.Contracts.Events;
using Skarbiec.Identity.Data;
using Skarbiec.Identity.Messaging;
using Skarbiec.ServiceDefaults.Messaging;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Identity.Tests;

/// <summary>
/// Proves the shared <see cref="IdempotentConsumerDefinition{TConsumer,TDbContext}"/> template
/// (T0.12, ADR-012) actually deduplicates by <c>MessageId</c>: redelivering the exact same message
/// must not run the consumer body twice. Builds its own provider (rather than
/// <see cref="IdentityApiFactory"/>) with a throwaway counting consumer on a queue name unique to
/// this test class, mirroring <c>UserRegisteredOutboxDurabilityTests</c>.
///
/// Verified once by swapping <see cref="CountingConsumerDefinition"/>'s base to a plain
/// <c>ConsumerDefinition&lt;CountingConsumer&gt;</c> (no inbox): the test goes red with
/// <c>ConsumeCount == 2</c>, confirming it actually exercises dedup rather than passing vacuously.
/// Reverted after confirming; not re-checked on every run.
/// </summary>
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
            // MassTransit's bus StartAsync returns once it has issued the receive endpoint's
            // queue/bind commands, but doesn't wait for RabbitMQ to have applied them — a publish
            // fired immediately after can race the exchange->queue binding and be dropped (fanout
            // exchanges don't hold messages for not-yet-bound queues). A short settle avoids that;
            // it's a test-harness timing issue, not something a real publisher needs to work around
            // (production publishes happen long after the consuming service's own startup).
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

            // Redeliver: same MessageId, same content. The inbox (InboxState, keyed on
            // MessageId + ConsumerId) must recognize it and skip the consumer body entirely.
            await bus.Publish(@event, ctx => ctx.MessageId = messageId, cancellationToken);

            // No signal to wait on for "it was skipped" — give a genuine redelivery time to land
            // before asserting it never did.
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

            // No UseBusOutbox() here: this test only exercises the consumer-side inbox
            // (UseEntityFrameworkOutbox on the receive endpoint, wired by
            // IdempotentConsumerDefinition), never IBus/IPublishEndpoint from a DI scope, so the
            // producer-side bus outbox and its background delivery poller would be dead weight.
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
