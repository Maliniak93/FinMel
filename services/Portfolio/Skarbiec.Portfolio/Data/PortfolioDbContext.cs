using MassTransit;
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
    public DbSet<Transaction> Transactions => Set<Transaction>();

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

            // Optimistic concurrency (T1.4 scope: two racing edits to the same asset's
            // transactions must not silently lose one's Quantity recompute). Maps to Postgres's
            // own xmin system column instead of an app-managed token — Postgres bumps it on every
            // UPDATE regardless of which handler runs, so RecordTransaction (T1.3) is covered too
            // without touching it. Shadow property: xmin already exists on every row, nothing to
            // migrate.
            asset.Property<uint>("Version")
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
        });

        modelBuilder.Entity<Transaction>(transaction =>
        {
            transaction.Property(t => t.Quantity).HasPrecision(18, 8);
            transaction.Property(t => t.UnitPriceAmount).HasPrecision(18, 2);
            transaction.Property(t => t.FeeAmount).HasPrecision(18, 2);

            // No navigation/FK to Asset (ADR-003) — every transaction query filters by AssetId.
            transaction.HasIndex(t => t.AssetId);

            // Same xmin concurrency token as Asset (see above) — protects a transaction row
            // itself against two concurrent edits/deletes of that exact transaction.
            transaction.Property<uint>("Version")
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
        });

        // Covers every IUserOwned entity added from here on without touching this method again (ADR-006).
        modelBuilder.ApplyUserOwnedQueryFilters(this);

        // MassTransit EF Outbox (T1.5, ADR-012), same pattern as Identity (T0.10): the bus outbox
        // writes here in the same SaveChanges call as the business entities above, so both commit
        // atomically. Table names prefixed with "Portfolio" — MassTransit's defaults ("InboxState"
        // etc.) collide with Identity's own outbox tables of the same name once both DbContexts'
        // migrations run against the single shared Postgres database Gateway.Tests uses to host
        // every service's test host side by side (Skarbiec.Testing's Testcontainer, not a
        // per-service database — ADR-003 db-per-service only holds for real deployments).
        modelBuilder.AddInboxStateEntity(x => x.ToTable("PortfolioInboxState"));
        modelBuilder.AddOutboxMessageEntity(x => x.ToTable("PortfolioOutboxMessage"));
        modelBuilder.AddOutboxStateEntity(x => x.ToTable("PortfolioOutboxState"));
    }
}
