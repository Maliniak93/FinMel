using Microsoft.EntityFrameworkCore;
using Skarbiec.ServiceDefaults.Authentication;
using Skarbiec.ServiceDefaults.Tenancy;
using Testcontainers.PostgreSql;

namespace Skarbiec.Portfolio.Tests;

/// <summary>
/// Proves the shared tenancy plumbing (ADR-006, T0.13) end to end against a real Postgres: an
/// entity implementing <see cref="IUserOwned"/>, inserted without ever setting <c>UserId</c>
/// manually, is visible to the user who created it and invisible to every other user. Uses its
/// own throwaway container/entity/DbContext instead of Portfolio's real one — Portfolio has no
/// entities yet, and mixing <c>EnsureCreated</c> here with Portfolio's own <c>Migrate</c> on the
/// same database would silently skip creating this table.
/// </summary>
public sealed class TenancyQueryFilterTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();

    public async ValueTask InitializeAsync() => await _postgres.StartAsync();

    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task EntityOwnedByUserA_IsInvisibleInContextScopedToUserB()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await using (var writerContext = CreateContext(userA))
        {
            await writerContext.Database.EnsureCreatedAsync(cancellationToken);

            // UserId is never set here — proves UserOwnedSaveInterceptor stamps it from the
            // current user, not from caller-supplied data (ADR-006).
            writerContext.Probes.Add(new TenancyProbe { Name = "owned-by-a" });
            await writerContext.SaveChangesAsync(cancellationToken);
        }

        await using var ownerContext = CreateContext(userA);
        await using var strangerContext = CreateContext(userB);

        Assert.Single(await ownerContext.Probes.ToListAsync(cancellationToken));
        Assert.Empty(await strangerContext.Probes.ToListAsync(cancellationToken));
    }

    private TenancyProbeDbContext CreateContext(Guid userId)
    {
        var options = new DbContextOptionsBuilder<TenancyProbeDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        return new TenancyProbeDbContext(options, new StubCurrentUser(userId));
    }
}

internal sealed class TenancyProbe : IUserOwned
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    public Guid UserId { get; set; }
}

internal sealed class TenancyProbeDbContext(DbContextOptions<TenancyProbeDbContext> options, ICurrentUser currentUser)
    : DbContext(options), ITenantScopedDbContext
{
    public DbSet<TenancyProbe> Probes => Set<TenancyProbe>();

    public Guid CurrentUserId => currentUser.UserId;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.AddInterceptors(new UserOwnedSaveInterceptor(currentUser));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyUserOwnedQueryFilters(this);
    }
}

internal sealed class StubCurrentUser(Guid userId) : ICurrentUser
{
    public bool IsAuthenticated => true;

    public Guid UserId => userId;
}
