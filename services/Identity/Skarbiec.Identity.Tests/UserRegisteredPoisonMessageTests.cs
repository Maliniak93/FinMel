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
/// Proves the other half of the shared <see cref="IdempotentConsumerDefinition{TConsumer,TDbContext}"/>
/// template (T0.12, ADR-012): a consumer that keeps failing exhausts its capped retry and the
/// message lands on the RabbitMQ transport's default <c>&lt;queue&gt;_error</c> queue instead of
/// being redelivered forever — and, crucially, the main queue keeps consuming other messages
/// afterwards rather than getting stuck behind the poison one.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class UserRegisteredPoisonMessageTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private const string QueueName = "user-registered-poison-test";
    private const string ErrorQueueName = $"{QueueName}_error";
    private const string PoisonEmail = "poison@example.com";

    public async ValueTask InitializeAsync()
    {
        await using var provider = BuildProducerProvider(new TaskCompletionSource<bool>());
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();

        await containers.ResetDatabaseAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Consume_AlwaysFailingMessage_EndsInErrorQueue_AndLaterMessagesStillFlow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var goodMessageReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var producerProvider = BuildProducerProvider(goodMessageReceived);
        var hostedServices = producerProvider.GetServices<IHostedService>().ToList();

        foreach (var hostedService in hostedServices)
        {
            await hostedService.StartAsync(cancellationToken);
        }

        var errorQueueReceived = new TaskCompletionSource<UserRegistered>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var errorObserver = await ErrorQueueObserver.StartAsync(containers.RabbitMqConnectionString, errorQueueReceived, cancellationToken);

        try
        {
            // See the identical comment in UserRegisteredIdempotentConsumerTests: a publish fired
            // immediately after StartAsync can race the receive endpoints' exchange->queue binding.
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);

            var bus = producerProvider.GetRequiredService<IBus>();

            var poisonEvent = new UserRegistered
            {
                UserId = Guid.NewGuid(),
                Email = PoisonEmail,
                DisplayName = "Poison Message",
                OccurredAtUtc = DateTimeOffset.UtcNow
            };

            await bus.Publish(poisonEvent, cancellationToken);

            var faulted = await errorQueueReceived.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            Assert.Equal(PoisonEmail, faulted.Email);

            // The queue must not be stuck behind the poisoned message: a message sent afterwards,
            // to the very same consumer/queue, still has to be consumed normally.
            var goodEvent = new UserRegistered
            {
                UserId = Guid.NewGuid(),
                Email = $"{Guid.NewGuid()}@example.com",
                DisplayName = "Ada Lovelace",
                OccurredAtUtc = DateTimeOffset.UtcNow
            };

            await bus.Publish(goodEvent, cancellationToken);

            await goodMessageReceived.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        }
        finally
        {
            for (var i = hostedServices.Count - 1; i >= 0; i--)
            {
                await hostedServices[i].StopAsync(cancellationToken);
            }
        }
    }

    private ServiceProvider BuildProducerProvider(TaskCompletionSource<bool> goodMessageReceived)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(goodMessageReceived);

        services.AddDbContext<IdentityDbContext>(options => options.UseNpgsql(containers.PostgresConnectionString));

        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();

            // No UseBusOutbox() here: this test only exercises the consumer-side inbox
            // (UseEntityFrameworkOutbox on the receive endpoint, wired by
            // IdempotentConsumerDefinition), never IBus/IPublishEndpoint from a DI scope, so the
            // producer-side bus outbox and its background delivery poller would be dead weight.
            x.AddEntityFrameworkOutbox<IdentityDbContext>(o => o.UsePostgres());

            x.AddConsumer<PoisonAwareConsumer>(typeof(FastRetryPoisonConsumerDefinition));

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(new Uri(containers.RabbitMqConnectionString));

                cfg.ConfigureEndpoints(context);
            });
        });

        return services.BuildServiceProvider();
    }

    private sealed class PoisonAwareConsumer(TaskCompletionSource<bool> goodMessageReceived) : IConsumer<UserRegistered>
    {
        public Task Consume(ConsumeContext<UserRegistered> context)
        {
            if (context.Message.Email == PoisonEmail)
            {
                throw new InvalidOperationException("Simulated poison message failure (T0.12 test).");
            }

            goodMessageReceived.TrySetResult(true);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Same idempotent-inbox template every real consumer uses, but with a fast, near-immediate
    /// retry policy so exhausting it (and faulting to the error queue) doesn't make this test slow.
    /// </summary>
    private sealed class FastRetryPoisonConsumerDefinition : IdempotentConsumerDefinition<PoisonAwareConsumer, IdentityDbContext>
    {
        public FastRetryPoisonConsumerDefinition() => Endpoint(e => e.Name = QueueName);

        protected override Action<IRetryConfigurator> RetryPolicy => r => r.Immediate(2);
    }

    /// <summary>
    /// A second, independent bus binding directly to the RabbitMQ transport's default
    /// <c>&lt;queue&gt;_error</c> queue — the destination MassTransit moves a message to once
    /// <see cref="FastRetryPoisonConsumerDefinition"/>'s retry is exhausted.
    /// </summary>
    private sealed class ErrorQueueObserver(ServiceProvider provider, IBusControl busControl) : IAsyncDisposable
    {
        public static async Task<ErrorQueueObserver> StartAsync(
            string rabbitMqConnectionString, TaskCompletionSource<UserRegistered> received, CancellationToken cancellationToken)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(received);
            services.AddMassTransit(x =>
            {
                x.SetKebabCaseEndpointNameFormatter();
                x.AddConsumer<ErrorQueueConsumer>();

                x.UsingRabbitMq((context, cfg) =>
                {
                    cfg.Host(new Uri(rabbitMqConnectionString));

                    cfg.ReceiveEndpoint(ErrorQueueName, e => e.ConfigureConsumer<ErrorQueueConsumer>(context));
                });
            });

            var provider = services.BuildServiceProvider();
            var busControl = provider.GetRequiredService<IBusControl>();
            await busControl.StartAsync(cancellationToken);

            return new ErrorQueueObserver(provider, busControl);
        }

        public async ValueTask DisposeAsync()
        {
            await busControl.StopAsync();
            await provider.DisposeAsync();
        }
    }

    private sealed class ErrorQueueConsumer(TaskCompletionSource<UserRegistered> received) : IConsumer<UserRegistered>
    {
        public Task Consume(ConsumeContext<UserRegistered> context)
        {
            received.TrySetResult(context.Message);
            return Task.CompletedTask;
        }
    }
}
