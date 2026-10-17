using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioConsumers;

namespace Skarbiec.Portfolio.Tests;

public sealed class ReplicaTenancyIsolationTests(SkarbiecContainersFixture containers) : IAsyncLifetime, IClassFixture<SkarbiecContainersFixture>
{
    public async ValueTask InitializeAsync() => await MigrateAndResetAsync(containers);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Query_AsUserB_ReturnsOnlyBsReplicas()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var savedAt = new DateTimeOffset(2026, 10, 15, 10, 0, 0, TimeSpan.Zero);

        await using (var seed = OpenDbContext(containers))
        {
            foreach (var userId in new[] { userA, userB })
            {
                seed.Set<TargetAllocationReplica>().Add(new TargetAllocationReplica { UserId = userId, Groups = [], SavedAtUtc = savedAt });
                seed.Set<ValuationReplica>().Add(new ValuationReplica
                {
                    UserId = userId,
                    Date = new DateOnly(2026, 10, 15),
                    IsStale = false,
                    TotalPln = 100m,
                    ByAssetClass = [],
                    OccurredAtUtc = savedAt,
                });
            }

            await seed.SaveChangesAsync(cancellationToken);
        }

        await using var asUserB = OpenDbContext(containers, userB);

        var targets = await asUserB.Set<TargetAllocationReplica>().ToListAsync(cancellationToken);
        var valuations = await asUserB.Set<ValuationReplica>().ToListAsync(cancellationToken);

        Assert.Equal(userB, Assert.Single(targets).UserId);
        Assert.Equal(userB, Assert.Single(valuations).UserId);
    }
}
