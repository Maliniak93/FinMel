using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts.Events;
using Skarbiec.Identity.Data;
using Skarbiec.Identity.Features.Register;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;
using Skarbiec.Testing.Messaging;

namespace Skarbiec.Identity.Tests;

/// <summary>
/// Proves the outbox row and the user row commit atomically (T0.10 AC). Deliberately builds its
/// own <see cref="ServiceProvider"/> instead of using <see cref="IdentityApiFactory"/>: no
/// <see cref="IHostedService"/> (MassTransit's bus, the outbox delivery poller) is ever started,
/// so the row can never be delivered/removed before the assertion runs — a real hosted bus would
/// race the poller against this check.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class UserRegisteredOutboxTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private ServiceProvider _provider = null!;

    public async ValueTask InitializeAsync()
    {
        _provider = HostlessOutboxProvider.Build<IdentityDbContext>(containers, services =>
        {
            services
                .AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = true)
                .AddEntityFrameworkStores<IdentityDbContext>();

            services.AddScoped<RegisterHandler>();
        });

        await using (var scope = _provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        }

        await containers.ResetDatabaseAsync();
    }

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task Register_WritesOutboxMessageInSameTransactionAsUserRow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _provider.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<RegisterHandler>();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var request = new RegisterRequest
        {
            Email = $"{Guid.NewGuid()}@example.com",
            Password = "Str0ng!Passw0rd",
            DisplayName = "Ada Lovelace"
        };

        var result = await handler.HandleAsync(request, cancellationToken);

        Assert.True(result.IsSuccess);

        var user = await dbContext.Users.SingleOrDefaultAsync(u => u.Id == result.Value.UserId, cancellationToken);
        Assert.NotNull(user);

        var outboxMessages = await dbContext.Set<OutboxMessage>().ToListAsync(cancellationToken);
        Assert.Contains(outboxMessages, m => m.MessageType.Contains(nameof(UserRegistered)));
    }
}
