using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.ServiceDefaults.Authentication;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class PortfolioDbContext(DbContextOptions<PortfolioDbContext> options, ICurrentUser currentUser)
    : DbContext(options), ITenantScopedDbContext
{
    public Guid CurrentUserId => currentUser.UserId;

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<AccountContributionLimit> AccountContributionLimits => Set<AccountContributionLimit>();
    public DbSet<CashPlace> CashPlaces => Set<CashPlace>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<TermDeposit> TermDeposits => Set<TermDeposit>();
    public DbSet<BankAccountTerms> BankAccountTerms => Set<BankAccountTerms>();
    public DbSet<TreasuryBond> TreasuryBonds => Set<TreasuryBond>();
    public DbSet<MetalHolding> MetalHoldings => Set<MetalHolding>();
    public DbSet<BankInterestSettlement> BankInterestSettlements => Set<BankInterestSettlement>();
    public DbSet<BondInterestSettlement> BondInterestSettlements => Set<BondInterestSettlement>();
    public DbSet<BondRedemption> BondRedemptions => Set<BondRedemption>();
    public DbSet<WatchlistItem> WatchlistItems => Set<WatchlistItem>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.AddInterceptors(new UserOwnedSaveInterceptor(currentUser));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Account>(account =>
        {
            account.Property(a => a.Name).HasMaxLength(200);
            account.Property(a => a.Type).HasConversion<string>().HasMaxLength(20);
            account.Property(a => a.Wrapper).HasConversion<string>().HasMaxLength(20);
            account.Property(a => a.Currency).HasMaxLength(3);

            // Case-sensitive exact match on Postgres's default collation; the client trims before submit.
            account.HasIndex(a => new { a.UserId, a.Name }).IsUnique();
        });

        modelBuilder.Entity<AccountContributionLimit>(limit =>
        {
            limit.HasIndex(l => new { l.AccountId, l.Year }).IsUnique();
            limit.Property(l => l.Amount).HasPrecision(18, 2);
        });

        modelBuilder.Entity<CashPlace>(place =>
        {
            place.Property(p => p.Name).HasMaxLength(200);

            place.HasIndex(p => new { p.UserId, p.Name }).IsUnique();
        });

        modelBuilder.Entity<Asset>(asset =>
        {
            asset.Property(a => a.Name).HasMaxLength(200);
            asset.Property(a => a.Currency).HasMaxLength(3);
            asset.Property(a => a.Quantity).HasPrecision(18, 8);

            // No FK, but every account-filtered asset query reads AccountId.
            asset.HasIndex(a => a.AccountId);

            // One balance per currency in a place; assets outside a place have a null CashPlaceId, which a unique index never collides.
            asset.HasIndex(a => new { a.CashPlaceId, a.Currency }).IsUnique();

            // No FK to MarketData either; indexed for the same reason.
            asset.HasIndex(a => a.InstrumentId);

            // xmin as the concurrency token, so two racing edits never silently lose a Quantity recompute.
            asset.Property<uint>("Xmin")
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
        });

        modelBuilder.Entity<Transaction>(transaction =>
        {
            transaction.Property(t => t.Quantity).HasPrecision(18, 8);
            transaction.Property(t => t.UnitPriceAmount).HasPrecision(18, 2);
            transaction.Property(t => t.FxRateToPln).HasPrecision(18, 8);
            transaction.Property(t => t.AmountPln).HasPrecision(18, 2);
            transaction.Property(t => t.FeeAmount).HasPrecision(18, 2);

            // No FK to Asset, but every transaction query filters by AssetId.
            transaction.HasIndex(t => t.AssetId);

            // A transfer's legs are found by their shared TransferId.
            transaction.HasIndex(t => t.TransferId);

            // Same xmin concurrency token as Asset, guarding concurrent edits of one transaction.
            transaction.Property<uint>("Xmin")
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
        });

        modelBuilder.Entity<TermDeposit>(termDeposit =>
        {
            // Unlike every other reference here a real FK: both rows live in portfolio_db and one never exists without the other.
            termDeposit.HasKey(t => t.AssetId);
            termDeposit.HasOne<Asset>()
                .WithOne()
                .HasForeignKey<TermDeposit>(t => t.AssetId)
                .OnDelete(DeleteBehavior.Cascade);

            termDeposit.Property(t => t.BankName).HasMaxLength(100);
            termDeposit.Property(t => t.Principal).HasPrecision(18, 2);
            termDeposit.Property(t => t.AnnualInterestRatePercent).HasPrecision(7, 4);
            termDeposit.Property(t => t.EarlyBreakInterestLossPercent).HasPrecision(5, 2);
            termDeposit.Property(t => t.SettledGrossInterest).HasPrecision(18, 2);
            termDeposit.Property(t => t.SettledTax).HasPrecision(18, 2);
        });

        modelBuilder.Entity<BankAccountTerms>(terms =>
        {
            // Keyed and FK-cascaded exactly like TermDeposit, to the account.
            terms.HasKey(t => t.AccountId);
            terms.HasOne<Account>()
                .WithOne()
                .HasForeignKey<BankAccountTerms>(t => t.AccountId)
                .OnDelete(DeleteBehavior.Cascade);

            terms.Property(t => t.Variant).HasConversion<string>().HasMaxLength(20);
            terms.Property(t => t.BankName).HasMaxLength(100);
            terms.Property(t => t.AnnualInterestRatePercent).HasPrecision(7, 4);
        });

        modelBuilder.Entity<MetalHolding>(metalHolding =>
        {
            // Keyed and FK-cascaded exactly like TermDeposit.
            metalHolding.HasKey(m => m.AssetId);
            metalHolding.HasOne<Asset>()
                .WithOne()
                .HasForeignKey<MetalHolding>(m => m.AssetId)
                .OnDelete(DeleteBehavior.Cascade);

            metalHolding.Property(m => m.FineWeightGramsPerPiece).HasPrecision(18, 8);
        });

        modelBuilder.Entity<TreasuryBond>(treasuryBond =>
        {
            // Keyed and FK-cascaded exactly like TermDeposit.
            treasuryBond.HasKey(b => b.AssetId);
            treasuryBond.HasOne<Asset>()
                .WithOne()
                .HasForeignKey<TreasuryBond>(b => b.AssetId)
                .OnDelete(DeleteBehavior.Cascade);

            treasuryBond.Property(b => b.SeriesCode).HasMaxLength(7);
            treasuryBond.Property(b => b.PurchasePricePerBond).HasPrecision(18, 2);
            treasuryBond.Property(b => b.FirstPeriodRatePercent).HasPrecision(7, 4);
            treasuryBond.Property(b => b.MarginPercent).HasPrecision(7, 4);
            treasuryBond.Property(b => b.EarlyRedemptionFeePerBond).HasPrecision(18, 2);
        });

        modelBuilder.Entity<BankInterestSettlement>(settlement =>
        {
            // No FK to the asset or the credit: RemoveAsset deletes the rows explicitly.
            settlement.HasIndex(s => new { s.AssetId, s.PeriodEnd }).IsUnique();

            // Update/DeleteTransaction ask whether a transaction is a settlement's managed credit.
            settlement.HasIndex(s => s.TransactionId);

            settlement.Property(s => s.GrossInterest).HasPrecision(18, 2);
            settlement.Property(s => s.Tax).HasPrecision(18, 2);
        });

        modelBuilder.Entity<BondInterestSettlement>(settlement =>
        {
            // No FK to the asset, the credit or the transfer: RemoveAsset deletes the rows explicitly.
            settlement.HasIndex(s => new { s.AssetId, s.PeriodIndex }).IsUnique();

            // ListTransactions marks a credit with its period through this column.
            settlement.HasIndex(s => s.CreditTransactionId);

            settlement.Property(s => s.RatePercent).HasPrecision(7, 4);
            settlement.Property(s => s.GrossInterest).HasPrecision(18, 2);
            settlement.Property(s => s.Tax).HasPrecision(18, 2);
        });

        modelBuilder.Entity<BondRedemption>(redemption =>
        {
            // No FK to the asset, the swap target or the transactions: RemoveAsset deletes the rows explicitly.
            // Not unique: early redemptions take a holding apart over time; the asset row's xmin guards a racing second one.
            redemption.HasIndex(r => r.AssetId);

            redemption.Property(r => r.CapitalisedInterest).HasPrecision(18, 2);
            redemption.Property(r => r.DiscountIncome).HasPrecision(18, 2);
            redemption.Property(r => r.AccruedInterest).HasPrecision(18, 2);
            redemption.Property(r => r.Fee).HasPrecision(18, 2);
            redemption.Property(r => r.Tax).HasPrecision(18, 2);
            redemption.Property(r => r.Proceeds).HasPrecision(18, 2);
        });

        modelBuilder.Entity<WatchlistItem>(item =>
        {
            // No FK to the MarketData instrument; the pair is the watch itself.
            item.HasIndex(i => new { i.UserId, i.InstrumentId }).IsUnique();

            item.Property(i => i.Ticker).HasMaxLength(50);
            item.Property(i => i.Name).HasMaxLength(200);
            item.Property(i => i.Exchange).HasMaxLength(50);
            item.Property(i => i.QuoteCurrency).HasMaxLength(3);
            item.Property(i => i.Note).HasMaxLength(500);
            item.Property(i => i.TargetPrice).HasPrecision(18, 8);
        });

        // Covers every IUserOwned entity added from here on without touching this method again.
        modelBuilder.ApplyUserOwnedQueryFilters(this);

        // Prefixed table names: MassTransit's defaults collide with Identity's outbox tables in the shared test database.
        modelBuilder.AddInboxStateEntity(x => x.ToTable("PortfolioInboxState"));
        modelBuilder.AddOutboxMessageEntity(x => x.ToTable("PortfolioOutboxMessage"));
        modelBuilder.AddOutboxStateEntity(x => x.ToTable("PortfolioOutboxState"));
    }
}
