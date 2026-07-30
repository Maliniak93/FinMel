using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Skarbiec.Contracts.Events;
using Skarbiec.Identity.Data;
using Skarbiec.Identity.Features.Register;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Identity.Tests;

/// <summary>
/// Proves the outbox survives a crash between commit and dispatch (T0.11 AC; E9: "killing the
/// process between commit and publish doesn't lose the event"). This is the whole point of routing
/// every publish through the outbox instead of calling the bus directly (ADR-012): the event's
/// durability lives in Postgres, not in the process's memory, so a second, wholly independent
/// process can pick up delivery later.
///
/// "Process 1" commits the user row and the outbox row in one transaction against a provider whose
/// bus and outbox delivery service never start — nothing in that provider is capable of dispatching
/// the message, simulating the process dying right after commit. "Process 2" is a fresh provider,
/// pointed at the very same database, standing in for a restarted instance: starting its hosted
/// services (bus + <c>BusOutboxDeliveryService</c>) is what finally delivers the still-pending row,
/// to a consumer on a queue name unique to this test (the shared RabbitMQ container's queues are
/// durable and outlive any one test class, so a shared/well-known queue name could pick up another
/// test's message instead of this one's).
///
/// Verified once by temporarily replacing the outbox publish in <see cref="RegisterHandler"/> with a
/// direct <c>IBus.Publish</c> call outside the transaction: with that change this test goes red
/// (nothing survives "process 1" dying, because there is no durable outbox row for "process 2" to
/// find) — confirming the test actually exercises the durability guarantee rather than passing
/// vacuously. Reverted after confirming; not re-checked on every run.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class UserRegisteredOutboxDurabilityTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private const string DurabilityTestQueueName = "user-registered-outbox-durability-test";

    public async ValueTask InitializeAsync()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();

        await containers.ResetDatabaseAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Register_SurvivesCrashBetweenCommitAndDispatch_DeliversAfterRestart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // MassTransit caches the *first* ILoggerFactory it sees for the whole process (not per
        // bus), so a bus built earlier in this test run whose provider has since been disposed
        // would otherwise leave every later bus in the process resolving a disposed factory.
        // Pointing it at a factory this test method owns and keeps alive for its whole duration
        // keeps the bus built below logging correctly regardless of what other tests already ran.
        using var loggerFactory = LoggerFactory.Create(builder => { });
        LogContext.ConfigureCurrentLogContext(loggerFactory);

        var request = new RegisterRequest
        {
            Email = $"{Guid.NewGuid()}@example.com",
            Password = "Str0ng!Passw0rd",
            DisplayName = "Ada Lovelace"
        };

        // "Process 1": commit, then die. This provider's bus/delivery service is never started, so
        // nothing here can dispatch the message to RabbitMQ - only the outbox row can carry it forward.
        await using (var crashedProvider = BuildProvider())
        {
            await using var scope = crashedProvider.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<RegisterHandler>();

            var result = await handler.HandleAsync(request, cancellationToken);
            Assert.True(result.IsSuccess);

            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var outboxMessages = await dbContext.Set<OutboxMessage>().ToListAsync(cancellationToken);
            Assert.Contains(outboxMessages, m => m.MessageType.Contains(nameof(UserRegistered)));
        }

        // "Process 2": an independent provider against the same database, with a consumer on a
        // queue name unique to this test. Starting its hosted services (MassTransit bus +
        // BusOutboxDeliveryService) is the "restart" that resumes delivery of the row process 1
        // left behind.
        var received = new TaskCompletionSource<UserRegistered>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var restartedProvider = BuildProvider(
            configureServices: services => services.AddSingleton(received),
            configureConsumers: x => x.AddConsumer<DurabilityTestConsumer>()
                .Endpoint(e => e.Name = DurabilityTestQueueName));

        var hostedServices = restartedProvider.GetServices<IHostedService>().ToList();

        foreach (var hostedService in hostedServices)
        {
            await hostedService.StartAsync(cancellationToken);
        }

        try
        {
            var receivedEvent = await received.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);

            Assert.Equal(request.Email, receivedEvent.Email);
            Assert.Equal(request.DisplayName, receivedEvent.DisplayName);

            await using var verifyScope = restartedProvider.CreateAsyncScope();
            var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var userCount = await verifyDbContext.Users.CountAsync(u => u.Email == request.Email, cancellationToken);
            Assert.Equal(1, userCount);
        }
        finally
        {
            for (var i = hostedServices.Count - 1; i >= 0; i--)
            {
                await hostedServices[i].StopAsync(cancellationToken);
            }
        }
    }

    private ServiceProvider BuildProvider(
        Action<IServiceCollection>? configureServices = null,
        Action<IBusRegistrationConfigurator>? configureConsumers = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDbContext<IdentityDbContext>(options => options.UseNpgsql(containers.PostgresConnectionString));

        services
            .AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = true)
            .AddEntityFrameworkStores<IdentityDbContext>();

        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();

            x.AddEntityFrameworkOutbox<IdentityDbContext>(o =>
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

        services.AddScoped<RegisterHandler>();
        configureServices?.Invoke(services);

        return services.BuildServiceProvider();
    }

    private sealed class DurabilityTestConsumer(TaskCompletionSource<UserRegistered> received) : IConsumer<UserRegistered>
    {
        public Task Consume(ConsumeContext<UserRegistered> context)
        {
            received.TrySetResult(context.Message);
            return Task.CompletedTask;
        }
    }
}
