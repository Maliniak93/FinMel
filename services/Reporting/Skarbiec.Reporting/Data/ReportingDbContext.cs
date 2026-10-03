using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.ServiceDefaults.Authentication;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Reporting.Data;

public sealed class ReportingDbContext(DbContextOptions<ReportingDbContext> options, ICurrentUser currentUser)
    : DbContext(options), ITenantScopedDbContext
{
    public Guid CurrentUserId => currentUser.UserId;

    public DbSet<Position> Positions => Set<Position>();
    public DbSet<AssetValuation> AssetValuations => Set<AssetValuation>();
    public DbSet<ValuationSnapshot> ValuationSnapshots => Set<ValuationSnapshot>();
    public DbSet<LatestInstrumentPrice> LatestInstrumentPrices => Set<LatestInstrumentPrice>();
    public DbSet<LatestFxRate> LatestFxRates => Set<LatestFxRate>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.AddInterceptors(new UserOwnedSaveInterceptor(currentUser));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Position>(position =>
        {
            // Portfolio's asset id is the key, so there is no surrogate and no second uniqueness rule.
            position.HasKey(p => p.AssetId);
            position.Property(p => p.AssetId).ValueGeneratedNever();

            position.Property(p => p.Currency).HasMaxLength(3);
            position.Property(p => p.Quantity).HasPrecision(18, 8);
            position.Property(p => p.ManualValueAmount).HasPrecision(18, 2);

            // No FK, but the archive, restore and delete consumers and the valuation grouping filter on it.
            position.HasIndex(p => p.PortfolioId);
        });

        modelBuilder.Entity<AssetValuation>(line =>
        {
            line.Property(l => l.Quantity).HasPrecision(18, 8);
            line.Property(l => l.PriceUsed).HasPrecision(18, 8);
            line.Property(l => l.FxRateUsed).HasPrecision(18, 8);
            line.Property(l => l.ValuePln).HasPrecision(18, 2);

            // The sync consumer upserts on this key, so a redelivery or a rerun overwrites instead of duplicating.
            line.HasIndex(l => new { l.AssetId, l.Date }).IsUnique();

            // The dashboard's per-class breakdown filters by user and latest date.
            line.HasIndex(l => new { l.UserId, l.Date });
        });

        modelBuilder.Entity<ValuationSnapshot>(snapshot =>
        {
            snapshot.Property(s => s.TotalPln).HasPrecision(18, 2);

            // One row per portfolio per day, so a redelivered event overwrites it.
            snapshot.HasIndex(s => new { s.PortfolioId, s.Date }).IsUnique();

            // GetDashboard and GetNetWorthHistory filter and order on this pair.
            snapshot.HasIndex(s => new { s.UserId, s.Date });
        });

        // Global reference data, not IUserOwned, so the query filter below skips it.
        modelBuilder.Entity<LatestInstrumentPrice>(price =>
        {
            price.HasKey(p => p.InstrumentId);
            price.Property(p => p.InstrumentId).ValueGeneratedNever();
            price.Property(p => p.QuoteCurrency).HasMaxLength(3);
            price.Property(p => p.Close).HasPrecision(18, 8);
        });

        modelBuilder.Entity<LatestFxRate>(rate =>
        {
            rate.HasKey(r => r.Pair);
            rate.Property(r => r.Pair).HasMaxLength(6);
            rate.Property(r => r.Rate).HasPrecision(18, 8);
        });

        // Covers every IUserOwned entity added from here on without touching this method again.
        modelBuilder.ApplyUserOwnedQueryFilters(this);

        // Prefixed table names: MassTransit's defaults collide with the other services' outbox tables in the shared test database.
        modelBuilder.AddInboxStateEntity(x => x.ToTable("ReportingInboxState"));
        modelBuilder.AddOutboxMessageEntity(x => x.ToTable("ReportingOutboxMessage"));
        modelBuilder.AddOutboxStateEntity(x => x.ToTable("ReportingOutboxState"));
    }
}
