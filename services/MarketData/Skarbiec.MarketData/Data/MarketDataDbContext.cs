using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Skarbiec.MarketData.Data;

// No tenancy filter: instruments and quotes are global reference data.
public sealed class MarketDataDbContext(DbContextOptions<MarketDataDbContext> options) : DbContext(options)
{
    public DbSet<Instrument> Instruments => Set<Instrument>();
    public DbSet<PriceQuote> PriceQuotes => Set<PriceQuote>();
    public DbSet<FxRate> FxRates => Set<FxRate>();
    public DbSet<SyncRun> SyncRuns => Set<SyncRun>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<InstrumentUsage> InstrumentUsages => Set<InstrumentUsage>();
    public DbSet<AssetInstrumentLink> AssetInstrumentLinks => Set<AssetInstrumentLink>();
    public DbSet<BondSeries> BondSeries => Set<BondSeries>();
    public DbSet<BondSeriesPeriodRate> BondSeriesPeriodRates => Set<BondSeriesPeriodRate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Instrument>(instrument =>
        {
            instrument.Property(i => i.Ticker).HasMaxLength(30);
            instrument.Property(i => i.Name).HasMaxLength(200);
            instrument.Property(i => i.QuoteCurrency).HasMaxLength(3);

            instrument.Property(i => i.VerificationStatus)
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValue(InstrumentVerificationStatus.Verified);

            // The same ticker can recur under another source, so the pair is the natural key.
            instrument.HasIndex(i => new { i.Source, i.Ticker }).IsUnique();
        });

        modelBuilder.Entity<PriceQuote>(quote =>
        {
            quote.Property(q => q.Close).HasPrecision(18, 8);

            quote.HasIndex(q => new { q.InstrumentId, q.Date }).IsUnique();
        });

        modelBuilder.Entity<FxRate>(rate =>
        {
            rate.Property(r => r.Pair).HasMaxLength(6);
            rate.Property(r => r.Rate).HasPrecision(18, 8);

            rate.HasIndex(r => new { r.Pair, r.Date }).IsUnique();
        });

        modelBuilder.Entity<SyncRun>(run =>
        {
            run.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);

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

        // Ids come from events, with no FK: an event can name an instrument before its row exists.
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

        // Type stays an int column, so ordering by it follows TreasuryBondType's member order.
        modelBuilder.Entity<BondSeries>(series =>
        {
            series.HasKey(s => s.Code);
            series.Property(s => s.Code).HasMaxLength(7);
            series.Property(s => s.Isin).HasMaxLength(12);
            series.Property(s => s.IssuePrice).HasPrecision(18, 2);
            series.Property(s => s.SwapPrice).HasPrecision(18, 2);
            series.Property(s => s.MarginPercent).HasPrecision(9, 2);

            series.HasMany(s => s.PeriodRates)
                .WithOne()
                .HasForeignKey(r => r.SeriesCode)
                .OnDelete(DeleteBehavior.Cascade);

            // ListBondSeries filters by the sale window.
            series.HasIndex(s => new { s.SaleStart, s.SaleEnd });
        });

        modelBuilder.Entity<BondSeriesPeriodRate>(rate =>
        {
            rate.HasKey(r => new { r.SeriesCode, r.PeriodIndex });
            rate.Property(r => r.SeriesCode).HasMaxLength(7);
            rate.Property(r => r.RatePercent).HasPrecision(9, 2);
        });

        // Prefixed table names: MassTransit's defaults collide with the other services' outbox tables in the shared test database.
        modelBuilder.AddInboxStateEntity(x => x.ToTable("MarketDataInboxState"));
        modelBuilder.AddOutboxMessageEntity(x => x.ToTable("MarketDataOutboxMessage"));
        modelBuilder.AddOutboxStateEntity(x => x.ToTable("MarketDataOutboxState"));
    }
}
