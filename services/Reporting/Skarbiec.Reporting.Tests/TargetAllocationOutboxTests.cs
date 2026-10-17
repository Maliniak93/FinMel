using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Features.TargetAllocation.SaveTargetAllocation;
using Skarbiec.Reporting.Tests.Fixtures;
using Skarbiec.ServiceDefaults.Authentication;
using Skarbiec.Testing.Containers;
using Skarbiec.Testing.Messaging;

namespace Skarbiec.Reporting.Tests;

// Builds its own provider so no hosted service starts and the outbox row is never delivered before the assertion reads it.
public sealed class TargetAllocationOutboxTests(SkarbiecContainersFixture containers) : IAsyncLifetime, IClassFixture<SkarbiecContainersFixture>
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
        });

        await using (var scope = _provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ReportingDbContext>().Database.MigrateAsync();
        }

        await containers.ResetDatabaseAsync();
    }

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task Save_ValidTarget_WritesTargetAllocationSavedWithTheStoredGroups()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var result = await scope.ServiceProvider.GetRequiredService<SaveTargetAllocationHandler>().HandleAsync(
            new SaveTargetAllocationRequest
            {
                Groups =
                [
                    Group(" Akcje/ETF ", [AssetClass.Stock, AssetClass.Etf], 50m),
                    Group("Obligacje", [AssetClass.Bond, AssetClass.Deposit], 40m),
                    Group("Kruszce", [AssetClass.PreciousMetal], 10m),
                ],
            },
            cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, await db.TargetAllocations.IgnoreQueryFilters().CountAsync(cancellationToken));

        var published = Assert.Single(await db.ReadPublishedAsync<TargetAllocationSaved>(cancellationToken));
        Assert.Equal(UserId, published.UserId);
        Assert.True(published.SavedAtUtc >= before);
        Assert.Equal(["Akcje/ETF", "Obligacje", "Kruszce"], published.Groups.Select(g => g.Name));
        Assert.Equal([AssetClass.Stock, AssetClass.Etf], published.Groups[0].AssetClasses);
        Assert.Equal([50m, 40m, 10m], published.Groups.Select(g => g.TargetPercent));
        Assert.All(published.Groups, g => Assert.Equal(5m, g.BandPp));
    }

    [Fact]
    public async Task Save_InvalidTarget_WritesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();

        var result = await scope.ServiceProvider.GetRequiredService<SaveTargetAllocationHandler>().HandleAsync(
            new SaveTargetAllocationRequest
            {
                Groups =
                [
                    Group("Akcje/ETF", [AssetClass.Stock, AssetClass.Etf], 50m),
                    Group("Obligacje", [AssetClass.Bond, AssetClass.Deposit], 40m),
                ],
            },
            cancellationToken);

        Assert.True(result.IsFailure);
        Assert.Empty(await db.ReadPublishedAsync<TargetAllocationSaved>(cancellationToken));
        Assert.Equal(0, await db.TargetAllocations.IgnoreQueryFilters().CountAsync(cancellationToken));
    }

    private static SaveTargetAllocationGroup Group(string name, AssetClass[] assetClasses, decimal targetPercent) =>
        new() { Name = name, AssetClasses = assetClasses, TargetPercent = targetPercent, BandPp = 5m };
}
