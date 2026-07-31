using Microsoft.EntityFrameworkCore;
using Skarbiec.ServiceDefaults.Authentication;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class PortfolioDbContext(DbContextOptions<PortfolioDbContext> options, ICurrentUser currentUser)
    : DbContext(options), ITenantScopedDbContext
{
    public Guid CurrentUserId => currentUser.UserId;

    public DbSet<PortfolioEntity> Portfolios => Set<PortfolioEntity>();
    public DbSet<Asset> Assets => Set<Asset>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.AddInterceptors(new UserOwnedSaveInterceptor(currentUser));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PortfolioEntity>(portfolio =>
        {
            portfolio.Property(p => p.Name).HasMaxLength(200);
            portfolio.Property(p => p.Description).HasMaxLength(1000);
            portfolio.Property(p => p.Currency).HasMaxLength(3);

            // Name unique per user (T1.1 scope: "decide and document") — case-sensitive exact
            // match on Postgres's default collation; the client is expected to trim before submit.
            portfolio.HasIndex(p => new { p.UserId, p.Name }).IsUnique();
        });

        modelBuilder.Entity<Asset>(asset =>
        {
            asset.Property(a => a.Name).HasMaxLength(200);
            asset.Property(a => a.Currency).HasMaxLength(3);
            asset.Property(a => a.Quantity).HasPrecision(18, 8);
            asset.Property(a => a.ManualValueAmount).HasPrecision(18, 2);

            // No navigation/FK to Portfolio — cross-aggregate reference by plain Guid (see the
            // AssetCount denormalization decision on Portfolio, T1.1), but still worth indexing
            // since every asset query in this service filters by PortfolioId.
            asset.HasIndex(a => a.PortfolioId);
        });

        // Covers every IUserOwned entity added from here on without touching this method again (ADR-006).
        modelBuilder.ApplyUserOwnedQueryFilters(this);
    }
}
