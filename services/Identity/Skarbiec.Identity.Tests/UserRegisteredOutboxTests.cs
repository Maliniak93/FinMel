using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts.Events;
using Skarbiec.Identity.Data;
using Skarbiec.Identity.Features.Register;
using Skarbiec.Testing.Containers;
using Skarbiec.Testing.Messaging;

namespace Skarbiec.Identity.Tests;

// Builds its own provider so no hosted service starts and the outbox row is never delivered before the assertion reads it.
public sealed class UserRegisteredOutboxTests(SkarbiecContainersFixture containers) : IAsyncLifetime, IClassFixture<SkarbiecContainersFixture>
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
