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
    public DbSet<TermDeposit> TermDeposits => Set<TermDeposit>();
    public DbSet<SavingsAccount> SavingsAccounts => Set<SavingsAccount>();
    public DbSet<TreasuryBond> TreasuryBonds => Set<TreasuryBond>();
    public DbSet<MetalHolding> MetalHoldings => Set<MetalHolding>();
    public DbSet<SavingsInterestSettlement> SavingsInterestSettlements => Set<SavingsInterestSettlement>();
    public DbSet<BondInterestSettlement> BondInterestSettlements => Set<BondInterestSettlement>();
    public DbSet<BondRedemption> BondRedemptions => Set<BondRedemption>();

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

            // Case-sensitive exact match on Postgres's default collation; the client trims before submit.
            portfolio.HasIndex(p => new { p.UserId, p.Name }).IsUnique();
        });

        modelBuilder.Entity<Asset>(asset =>
        {
            asset.Property(a => a.Name).HasMaxLength(200);
            asset.Property(a => a.Currency).HasMaxLength(3);
            asset.Property(a => a.Quantity).HasPrecision(18, 8);
            asset.Property(a => a.ManualValueAmount).HasPrecision(18, 2);

            // No FK, but every asset query filters by PortfolioId.
            asset.HasIndex(a => a.PortfolioId);

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

        modelBuilder.Entity<SavingsAccount>(savingsAccount =>
        {
            // Keyed and FK-cascaded exactly like TermDeposit.
            savingsAccount.HasKey(s => s.AssetId);
            savingsAccount.HasOne<Asset>()
                .WithOne()
                .HasForeignKey<SavingsAccount>(s => s.AssetId)
                .OnDelete(DeleteBehavior.Cascade);

            savingsAccount.Property(s => s.BankName).HasMaxLength(100);
            savingsAccount.Property(s => s.AnnualInterestRatePercent).HasPrecision(7, 4);
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

        modelBuilder.Entity<SavingsInterestSettlement>(settlement =>
        {
            // No FK to the asset or the credit: RemoveAsset and DeletePortfolio delete the rows explicitly.
            settlement.HasIndex(s => new { s.AssetId, s.PeriodEnd }).IsUnique();

            // Update/DeleteTransaction ask whether a transaction is a settlement's managed credit.
            settlement.HasIndex(s => s.TransactionId);

            settlement.Property(s => s.GrossInterest).HasPrecision(18, 2);
            settlement.Property(s => s.Tax).HasPrecision(18, 2);
        });

        modelBuilder.Entity<BondInterestSettlement>(settlement =>
        {
            // No FK to the asset, the credit or the transfer: RemoveAsset and DeletePortfolio delete the rows explicitly.
            settlement.HasIndex(s => new { s.AssetId, s.PeriodIndex }).IsUnique();

            // ListTransactions marks a credit with its period through this column.
            settlement.HasIndex(s => s.CreditTransactionId);

            settlement.Property(s => s.RatePercent).HasPrecision(7, 4);
            settlement.Property(s => s.GrossInterest).HasPrecision(18, 2);
            settlement.Property(s => s.Tax).HasPrecision(18, 2);
        });

        modelBuilder.Entity<BondRedemption>(redemption =>
        {
            // No FK to the asset, the swap target or the transactions: RemoveAsset and DeletePortfolio delete the rows explicitly.
            // Not unique: early redemptions take a holding apart over time; the asset row's xmin guards a racing second one.
            redemption.HasIndex(r => r.AssetId);

            redemption.Property(r => r.CapitalisedInterest).HasPrecision(18, 2);
            redemption.Property(r => r.DiscountIncome).HasPrecision(18, 2);
            redemption.Property(r => r.AccruedInterest).HasPrecision(18, 2);
            redemption.Property(r => r.Fee).HasPrecision(18, 2);
            redemption.Property(r => r.Tax).HasPrecision(18, 2);
            redemption.Property(r => r.Proceeds).HasPrecision(18, 2);
        });

        // Covers every IUserOwned entity added from here on without touching this method again.
        modelBuilder.ApplyUserOwnedQueryFilters(this);

        // Prefixed table names: MassTransit's defaults collide with Identity's outbox tables in the shared test database.
        modelBuilder.AddInboxStateEntity(x => x.ToTable("PortfolioInboxState"));
        modelBuilder.AddOutboxMessageEntity(x => x.ToTable("PortfolioOutboxMessage"));
        modelBuilder.AddOutboxStateEntity(x => x.ToTable("PortfolioOutboxState"));
    }
}
