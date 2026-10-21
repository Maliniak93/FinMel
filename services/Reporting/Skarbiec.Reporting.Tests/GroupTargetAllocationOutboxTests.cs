using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Features.TargetAllocation.SaveGroupTargetAllocation;
using Skarbiec.Reporting.Features.TargetAllocation.SaveTargetAllocation;
using Skarbiec.Reporting.Tests.Fixtures;
using Skarbiec.ServiceDefaults.Authentication;
using Skarbiec.Testing.Containers;
using Skarbiec.Testing.Messaging;

namespace Skarbiec.Reporting.Tests;

// Builds its own provider so no hosted service starts and an outbox row would never be delivered before the assertion reads it.
public sealed class GroupTargetAllocationOutboxTests(SkarbiecContainersFixture containers) : IAsyncLifetime, IClassFixture<SkarbiecContainersFixture>
{
    private static readonly Guid UserId = Guid.NewGuid();

    private ServiceProvider _provider = null!;

    public async ValueTask InitializeAsync()
    {
        _provider = HostlessOutboxProvider.Build<ReportingDbContext>(containers, services =>
        {
            services.AddSingleton<ICurrentUser>(new StubCurrentUser(UserId));
            services.AddSingleton(TimeProvider.System);
            services.AddScoped<SaveTargetAllocationHandler>();
            services.AddScoped<SaveGroupTargetAllocationHandler>();
        });

        await using (var scope = _provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ReportingDbContext>().Database.MigrateAsync();
        }

        await containers.ResetDatabaseAsync();
    }

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task Save_PublishesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        var global = await scope.ServiceProvider.GetRequiredService<SaveTargetAllocationHandler>().HandleAsync(
            new SaveTargetAllocationRequest { Groups = [Group("Globalna", [AssetClass.Stock], 100m)] }, cancellationToken);
        Assert.True(global.IsSuccess);
        var publishedBefore = (await db.ReadPublishedAsync<TargetAllocationSaved>(cancellationToken)).Count;

        var saved = await scope.ServiceProvider.GetRequiredService<SaveGroupTargetAllocationHandler>().HandleAsync(
            Guid.NewGuid(),
            new SaveTargetAllocationRequest { Groups = [Group("Grupowa", [AssetClass.Bond], 100m)] },
            cancellationToken);

        Assert.True(saved.IsSuccess);
        Assert.Equal(1, await db.GroupTargetAllocations.IgnoreQueryFilters().CountAsync(cancellationToken));
        Assert.Equal(publishedBefore, (await db.ReadPublishedAsync<TargetAllocationSaved>(cancellationToken)).Count);
        var untouched = await db.TargetAllocations.IgnoreQueryFilters().AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal("Globalna", Assert.Single(untouched.Groups).Name);
    }

    private static SaveTargetAllocationGroup Group(string name, AssetClass[] assetClasses, decimal targetPercent) =>
        new() { Name = name, AssetClasses = assetClasses, TargetPercent = targetPercent, BandPp = 5m };
}
