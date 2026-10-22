using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
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
    public DbSet<AssetGroup> AssetGroups => Set<AssetGroup>();
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
    public DbSet<RecurringIncome> RecurringIncomes => Set<RecurringIncome>();
    public DbSet<RecurringIncomeVersion> RecurringIncomeVersions => Set<RecurringIncomeVersion>();
    public DbSet<RecurringIncomeOccurrence> RecurringIncomeOccurrences => Set<RecurringIncomeOccurrence>();
    public DbSet<RecurringExpense> RecurringExpenses => Set<RecurringExpense>();
    public DbSet<RecurringExpenseVersion> RecurringExpenseVersions => Set<RecurringExpenseVersion>();
    public DbSet<OneOffIncome> OneOffIncomes => Set<OneOffIncome>();
    public DbSet<OneOffExpense> OneOffExpenses => Set<OneOffExpense>();
    public DbSet<ValuationReplica> ValuationReplicas => Set<ValuationReplica>();
    public DbSet<PlanningSettings> PlanningSettings => Set<PlanningSettings>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.AddInterceptors(new UserOwnedSaveInterceptor(currentUser));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasSequence<long>("transaction_entry_order");

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

        // The case-insensitive (UserId, lower(Name)) unique index is raw SQL in the AssetGroups migration: the model cannot express it.
        modelBuilder.Entity<AssetGroup>(group =>
        {
            group.Property(g => g.Name).HasMaxLength(50);
            group.Property(g => g.Color).HasMaxLength(20);
            group.Property(g => g.TargetPln).HasPrecision(18, 2);
            group.Property(g => g.Kind).HasConversion<string>().HasMaxLength(20).HasDefaultValue(AssetGroupKind.Regular);
            group.Property(g => g.MonthlyContributionPln).HasPrecision(18, 2);
            group.ComplexCollection(g => g.ClassAllocation, allocation => allocation.ToJson());

            group.HasIndex(g => new { g.UserId, g.Color }).IsUnique();
        });

        modelBuilder.Entity<Asset>(asset =>
        {
            asset.Property(a => a.Name).HasMaxLength(200);

            // No FK to AssetGroup; indexed because the group's assets are read and cleared by GroupId.
            asset.HasIndex(a => a.GroupId);
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

            transaction.Property(t => t.EntryOrder)
                .HasDefaultValueSql("nextval('transaction_entry_order')")
                .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

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

        modelBuilder.Entity<RecurringIncome>(income =>
        {
            income.Property(i => i.Name).HasMaxLength(200);

            income.HasIndex(i => new { i.UserId, i.Name }).IsUnique();

            income.HasMany(i => i.Versions)
                .WithOne()
                .HasForeignKey(v => v.RecurringIncomeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RecurringIncomeVersion>(version =>
        {
            version.Property(v => v.Amount).HasPrecision(18, 2);
            version.Property(v => v.DayKind).HasConversion<string>().HasMaxLength(20);
            version.Property(v => v.Shift).HasConversion<string>().HasMaxLength(20);

            // Restrict: an account that an income targets is never deleted under it.
            version.HasOne<Account>()
                .WithMany()
                .HasForeignKey(v => v.AccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RecurringIncomeOccurrence>(occurrence =>
        {
            occurrence.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            occurrence.Property(o => o.PlannedAmount).HasPrecision(18, 2);
            occurrence.Property(o => o.ActualAmount).HasPrecision(18, 2);
            occurrence.Property(o => o.FxRateToPln).HasPrecision(18, 6);

            occurrence.HasIndex(o => new { o.RecurringIncomeId, o.NominalMonth }).IsUnique();

            occurrence.HasOne<RecurringIncome>()
                .WithMany()
                .HasForeignKey(o => o.RecurringIncomeId)
                .OnDelete(DeleteBehavior.Cascade);

            // Restrict: an account that an occurrence plans or settles to is never deleted under it.
            occurrence.HasOne<Account>()
                .WithMany()
                .HasForeignKey(o => o.PlannedAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            occurrence.HasOne<Account>()
                .WithMany()
                .HasForeignKey(o => o.AccountId)
                .OnDelete(DeleteBehavior.Restrict);

            // Restrict: a transaction an occurrence references is never deleted under it; the index also serves the transaction-lock lookups.
            occurrence.HasOne<Transaction>()
                .WithMany()
                .HasForeignKey(o => o.TransactionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RecurringExpense>(expense =>
        {
            expense.Property(e => e.Name).HasMaxLength(200);

            expense.HasIndex(e => new { e.UserId, e.Name }).IsUnique();

            expense.HasMany(e => e.Versions)
                .WithOne()
                .HasForeignKey(v => v.RecurringExpenseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RecurringExpenseVersion>(version =>
        {
            version.Property(v => v.Amount).HasPrecision(18, 2);
            version.Property(v => v.DayKind).HasConversion<string>().HasMaxLength(20);
            version.Property(v => v.Shift).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<OneOffIncome>(income =>
        {
            income.Property(i => i.Name).HasMaxLength(200);
            income.Property(i => i.Amount).HasPrecision(18, 2);
            income.Property(i => i.ActualAmount).HasPrecision(18, 2);
            income.Property(i => i.FxRateToPln).HasPrecision(18, 6);

            // Restrict: an account that a one-off income targets is never deleted under it.
            income.HasOne<Account>()
                .WithMany()
                .HasForeignKey(i => i.AccountId)
                .OnDelete(DeleteBehavior.Restrict);

            income.HasOne<Account>()
                .WithMany()
                .HasForeignKey(i => i.ActualAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            // Restrict: a transaction a settlement references is never deleted under it; the index also serves the transaction-lock lookups.
            income.HasOne<Transaction>()
                .WithMany()
                .HasForeignKey(i => i.TransactionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OneOffExpense>(expense =>
        {
            expense.Property(e => e.Name).HasMaxLength(200);
            expense.Property(e => e.Amount).HasPrecision(18, 2);
        });

        modelBuilder.Entity<ValuationReplica>(replica =>
        {
            replica.HasKey(r => r.UserId);
            replica.Property(r => r.UserId).ValueGeneratedNever();
            replica.Property(r => r.TotalPln).HasPrecision(18, 2);

            replica.ComplexCollection(r => r.ByAssetClass, byClass => byClass.ToJson());
        });

        modelBuilder.Entity<PlanningSettings>(settings =>
        {
            settings.HasKey(s => s.UserId);
            settings.Property(s => s.UserId).ValueGeneratedNever();
        });

        // Covers every IUserOwned entity added from here on without touching this method again.
        modelBuilder.ApplyUserOwnedQueryFilters(this);

        // Prefixed table names: MassTransit's defaults collide with Identity's outbox tables in the shared test database.
        modelBuilder.AddInboxStateEntity(x => x.ToTable("PortfolioInboxState"));
        modelBuilder.AddOutboxMessageEntity(x => x.ToTable("PortfolioOutboxMessage"));
        modelBuilder.AddOutboxStateEntity(x => x.ToTable("PortfolioOutboxState"));
    }
}
