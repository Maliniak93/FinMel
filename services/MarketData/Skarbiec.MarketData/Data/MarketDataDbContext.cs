using AppAny.Quartz.EntityFrameworkCore.Migrations;
using AppAny.Quartz.EntityFrameworkCore.Migrations.PostgreSQL;
using Microsoft.EntityFrameworkCore;

namespace Skarbiec.MarketData.Data;

// No tenancy filter here: instruments/quotes are global reference data, not user-owned (ADR-006
// scope note in T0.13 — custom per-user instruments are revisited in Phase 2).
public sealed class MarketDataDbContext(DbContextOptions<MarketDataDbContext> options) : DbContext(options)
{
    public DbSet<Instrument> Instruments => Set<Instrument>();
    public DbSet<PriceQuote> PriceQuotes => Set<PriceQuote>();
    public DbSet<FxRate> FxRates => Set<FxRate>();
    public DbSet<SyncRun> SyncRuns => Set<SyncRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Quartz's ADO job-store schema (T2.6), created via this DbContext's own migrations instead
        // of hand-running Quartz's tables_postgres.sql as a separate deploy step — schema "quartz",
        // table prefix "qrtz_" (AppAny package defaults). PriceSyncJobExtensions' UsePersistentStore
        // must use the matching schema-qualified prefix "quartz.qrtz_".
        modelBuilder.AddQuartz(quartz => quartz.UsePostgreSql());

        modelBuilder.Entity<Instrument>(instrument =>
        {
            instrument.Property(i => i.Ticker).HasMaxLength(30);
            instrument.Property(i => i.Name).HasMaxLength(200);
            instrument.Property(i => i.QuoteCurrency).HasMaxLength(3);

            // Natural key of the dictionary — same ticker can recur under a different source, so
            // the pair is what must stay unique. Also what keeps the seeder's idempotency check
            // (MarketDataSeeder) cheap and DB-enforced, not just application-level.
            instrument.HasIndex(i => new { i.Source, i.Ticker }).IsUnique();
        });

        modelBuilder.Entity<PriceQuote>(quote =>
        {
            quote.Property(q => q.Close).HasPrecision(18, 8);

            // Upsert-friendly: PriceSyncJob (T2.6) writes one row per instrument per day.
            quote.HasIndex(q => new { q.InstrumentId, q.Date }).IsUnique();
        });

        modelBuilder.Entity<FxRate>(rate =>
        {
            rate.Property(r => r.Pair).HasMaxLength(6);
            rate.Property(r => r.Rate).HasPrecision(18, 8);

            // Upsert-friendly: PriceSyncJob (T2.6) writes one row per pair per day.
            rate.HasIndex(r => new { r.Pair, r.Date }).IsUnique();
        });

        modelBuilder.Entity<SyncRun>(run =>
        {
            run.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        });
    }
}
