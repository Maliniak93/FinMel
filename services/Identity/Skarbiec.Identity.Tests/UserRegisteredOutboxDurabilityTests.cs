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
using Skarbiec.Testing.Containers;
using Skarbiec.Testing.Messaging;

namespace Skarbiec.Identity.Tests;

public sealed class UserRegisteredOutboxDurabilityTests(SkarbiecContainersFixture containers) : IAsyncLifetime, IClassFixture<SkarbiecContainersFixture>
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

        // MassTransit caches the first ILoggerFactory process-wide, so this test owns one that outlives its bus.
        using var loggerFactory = LoggerFactory.Create(builder => { });
        LogContext.ConfigureCurrentLogContext(loggerFactory);

        var request = new RegisterRequest
        {
            Email = $"{Guid.NewGuid()}@example.com",
            Password = "Str0ng!Passw0rd",
            DisplayName = "Ada Lovelace"
        };

        // Process 1 commits and dies: its bus never starts, so only the outbox row can carry the event.
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

        // Process 2: a fresh provider on the same database, whose hosted services deliver the pending row to a queue unique to this test.
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
        => HostlessOutboxProvider.Build<IdentityDbContext>(containers, services =>
        {
            services
                .AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = true)
                .AddEntityFrameworkStores<IdentityDbContext>();

            services.AddScoped<RegisterHandler>();
            configureServices?.Invoke(services);
        }, configureConsumers);

    private sealed class DurabilityTestConsumer(TaskCompletionSource<UserRegistered> received) : IConsumer<UserRegistered>
    {
        public Task Consume(ConsumeContext<UserRegistered> context)
        {
            received.TrySetResult(context.Message);
            return Task.CompletedTask;
        }
    }
}
