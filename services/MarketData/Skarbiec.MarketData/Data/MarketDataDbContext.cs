using AppAny.Quartz.EntityFrameworkCore.Migrations;
using AppAny.Quartz.EntityFrameworkCore.Migrations.PostgreSQL;
using MassTransit;
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
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<InstrumentUsage> InstrumentUsages => Set<InstrumentUsage>();
    public DbSet<AssetInstrumentLink> AssetInstrumentLinks => Set<AssetInstrumentLink>();

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

            // HasDefaultValue backfills every pre-existing row to Verified when the migration runs
            // (T2.8) — the dictionary predates verification tracking and was curated by hand.
            instrument.Property(i => i.VerificationStatus)
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValue(InstrumentVerificationStatus.Verified);

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

            // Upsert-friendly: FxSyncJob (spec-04) writes one row per pair per day.
            rate.HasIndex(r => new { r.Pair, r.Date }).IsUnique();
        });

        modelBuilder.Entity<SyncRun>(run =>
        {
            run.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);

            // Default backfills every pre-spec-04 row to Prices — PriceSyncJob was the only writer.
            run.Property(r => r.Kind)
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValue(SyncRunKind.Prices);

            // GetSyncStatus reads the latest run per kind.
            run.HasIndex(r => new { r.Kind, r.StartedAt }).IsDescending(false, true);
        });

        modelBuilder.Entity<Currency>(currency =>
        {
            currency.HasKey(c => c.Code);
            currency.Property(c => c.Code).HasMaxLength(3).IsFixedLength();
            currency.Property(c => c.Name).HasMaxLength(100);
            currency.Property(c => c.Symbol).HasMaxLength(10);
        });

        // InstrumentId/AssetId are ids reported by events, never generated here — and no FK to
        // Instrument: a Portfolio event can name an instrument before this row exists.
        modelBuilder.Entity<InstrumentUsage>(usage =>
        {
            usage.HasKey(u => u.InstrumentId);
            usage.Property(u => u.InstrumentId).ValueGeneratedNever();
        });

        modelBuilder.Entity<AssetInstrumentLink>(link =>
        {
            link.HasKey(l => l.AssetId);
            link.Property(l => l.AssetId).ValueGeneratedNever();

            // The usage consumers recount links per instrument on every change.
            link.HasIndex(l => l.InstrumentId);
        });

        // MassTransit EF Outbox (T2.10, ADR-012), same pattern as Identity (T0.10) and Portfolio
        // (T1.5): PriceSyncJob's completion write and the DailyPricesSynced outbox row commit
        // atomically. Table names prefixed "MarketData" — MassTransit's defaults ("InboxState" etc.)
        // would otherwise collide with the other services' own outbox tables once every DbContext's
        // migrations run against the single shared Postgres database Gateway.Tests uses to host every
        // service's test host side by side.
        modelBuilder.AddInboxStateEntity(x => x.ToTable("MarketDataInboxState"));
        modelBuilder.AddOutboxMessageEntity(x => x.ToTable("MarketDataOutboxMessage"));
        modelBuilder.AddOutboxStateEntity(x => x.ToTable("MarketDataOutboxState"));
    }
}
