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

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.AddInterceptors(new UserOwnedSaveInterceptor(currentUser));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Position>(position =>
        {
            // Portfolio's own asset id is the key — one row per asset by definition, no surrogate
            // and so no second uniqueness rule to enforce (spec-03 design decision 3).
            position.HasKey(p => p.AssetId);
            position.Property(p => p.AssetId).ValueGeneratedNever();

            position.Property(p => p.Currency).HasMaxLength(3);
            position.Property(p => p.Quantity).HasPrecision(18, 8);
            position.Property(p => p.ManualValueAmount).HasPrecision(18, 2);

            // No FK to Portfolio (ADR-003), but the archive/restore and delete consumers and the
            // daily valuation's per-portfolio grouping all filter on it.
            position.HasIndex(p => p.PortfolioId);
        });

        modelBuilder.Entity<AssetValuation>(line =>
        {
            line.Property(l => l.Quantity).HasPrecision(18, 8);
            line.Property(l => l.PriceUsed).HasPrecision(18, 8);
            line.Property(l => l.FxRateUsed).HasPrecision(18, 8);
            line.Property(l => l.ValuePln).HasPrecision(18, 2);

            // One line per asset per date (03-domain-model.md §Invariants): the DailyPricesSynced
            // consumer upserts into this key, so a redelivery or a rerun overwrites instead of
            // duplicating.
            line.HasIndex(l => new { l.AssetId, l.Date }).IsUnique();

            // The dashboard's ByAssetClass breakdown filters by user and the latest date per
            // portfolio — same rationale as the snapshot index below.
            line.HasIndex(l => new { l.UserId, l.Date });
        });

        modelBuilder.Entity<ValuationSnapshot>(snapshot =>
        {
            snapshot.Property(s => s.TotalPln).HasPrecision(18, 2);

            // Upsert-friendly, same shape as MarketData's (instrument, date)/(pair, date): the
            // DailyPricesSynced consumer writes one row per portfolio per day, and a redelivered
            // event overwrites the same row instead of duplicating it.
            snapshot.HasIndex(s => new { s.PortfolioId, s.Date }).IsUnique();

            // T2.12: GetDashboard (latest per portfolio for the current user) and
            // GetNetWorthHistory (date-range scan for the current user) both filter/order on
            // exactly this pair — the P95 <1s AC's seeded-year scale needs this to stay an index
            // scan instead of a sequential one.
            snapshot.HasIndex(s => new { s.UserId, s.Date });
        });

        // Covers every IUserOwned entity added from here on without touching this method again (ADR-006).
        modelBuilder.ApplyUserOwnedQueryFilters(this);

        // MassTransit EF Outbox/Inbox (T2.11, ADR-012), same pattern as Identity/Portfolio/MarketData:
        // every consumer's writes (Position upserts, AssetValuation lines, ValuationSnapshot rows)
        // and its InboxState dedup row commit in the one SaveChanges call the consumer itself makes
        // — which is why none of them use ExecuteUpdate/ExecuteDelete. Table names prefixed
        // "Reporting" — see the sibling DbContexts for why (shared Postgres container in tests).
        modelBuilder.AddInboxStateEntity(x => x.ToTable("ReportingInboxState"));
        modelBuilder.AddOutboxMessageEntity(x => x.ToTable("ReportingOutboxMessage"));
        modelBuilder.AddOutboxStateEntity(x => x.ToTable("ReportingOutboxState"));
    }
}
