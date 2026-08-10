using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.ServiceDefaults.Authentication;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Reporting.Data;

public sealed class ReportingDbContext(DbContextOptions<ReportingDbContext> options, ICurrentUser currentUser)
    : DbContext(options), ITenantScopedDbContext
{
    public Guid CurrentUserId => currentUser.UserId;

    public DbSet<ValuationSnapshot> ValuationSnapshots => Set<ValuationSnapshot>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.AddInterceptors(new UserOwnedSaveInterceptor(currentUser));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ValuationSnapshot>(snapshot =>
        {
            snapshot.Property(s => s.TotalPln).HasPrecision(18, 2);

            // First JSONB column in the project (T2.11) — see ValuationBreakdown for why it's a
            // plain serialized string column instead of EF's own JSON-owned-entity mapping.
            snapshot.Property(s => s.BreakdownJson).HasColumnType("jsonb");

            // Upsert-friendly, same shape as MarketData's (instrument, date)/(pair, date): the
            // DailyPricesSynced consumer (T2.11) writes one row per portfolio per day, and a
            // redelivered event overwrites the same row instead of duplicating it.
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
        // the DailyPricesSynced consumer's ValuationSnapshot writes and its InboxState dedup row
        // commit in the one SaveChanges call the consumer itself makes. Table names prefixed
        // "Reporting" — see the sibling DbContexts for why (shared Postgres container in tests).
        modelBuilder.AddInboxStateEntity(x => x.ToTable("ReportingInboxState"));
        modelBuilder.AddOutboxMessageEntity(x => x.ToTable("ReportingOutboxMessage"));
        modelBuilder.AddOutboxStateEntity(x => x.ToTable("ReportingOutboxState"));
    }
}
