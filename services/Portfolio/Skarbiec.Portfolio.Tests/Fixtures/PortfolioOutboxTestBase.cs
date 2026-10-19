using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.Accounts.ArchiveAccount;
using Skarbiec.Portfolio.Features.Accounts.BankInterest.SettleBankInterest;
using Skarbiec.Portfolio.Features.Accounts.BankInterest.UndoBankInterestSettlement;
using Skarbiec.Portfolio.Features.Accounts.CreateAccount;
using Skarbiec.Portfolio.Features.Accounts.DeleteAccount;
using Skarbiec.Portfolio.Features.Accounts.RestoreAccount;
using Skarbiec.Portfolio.Features.Accounts.UpdateAccount;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.ArchiveAsset;
using Skarbiec.Portfolio.Features.AssetGroups.AssignAssetsToGroup;
using Skarbiec.Portfolio.Features.AssetGroups.CreateAssetGroup;
using Skarbiec.Portfolio.Features.AssetGroups.DeleteAssetGroup;
using Skarbiec.Portfolio.Features.AssetGroups.UpdateAssetGroup;
using Skarbiec.Portfolio.Features.Bonds.AddBond;
using Skarbiec.Portfolio.Features.Bonds.RedeemBond;
using Skarbiec.Portfolio.Features.Bonds.RedeemBondEarly;
using Skarbiec.Portfolio.Features.Bonds.SettleBondInterest;
using Skarbiec.Portfolio.Features.Bonds.SwapBond;
using Skarbiec.Portfolio.Features.Bonds.UndoBondInterestSettlement;
using Skarbiec.Portfolio.Features.Bonds.UpdateBond;
using Skarbiec.Portfolio.Features.CashPlaces.CreateCashPlace;
using Skarbiec.Portfolio.Features.CashPlaces.DeleteCashPlace;
using Skarbiec.Portfolio.Features.CashPlaces.UpdateCashPlace;
using Skarbiec.Portfolio.Features.DeleteTransaction;
using Skarbiec.Portfolio.Features.Deposits.AddDeposit;
using Skarbiec.Portfolio.Features.Deposits.PayOutDeposit;
using Skarbiec.Portfolio.Features.Deposits.RollOverDeposit;
using Skarbiec.Portfolio.Features.Deposits.SettleDeposit;
using Skarbiec.Portfolio.Features.Deposits.UpdateDeposit;
using Skarbiec.Portfolio.Features.Metals.AddMetal;
using Skarbiec.Portfolio.Features.Metals.UpdateMetal;
using Skarbiec.Portfolio.Features.RecordTransaction;
using Skarbiec.Portfolio.Features.RecurringIncomes.CreateRecurringIncome;
using Skarbiec.Portfolio.Features.RecurringIncomes.Occurrences.SettleOccurrence;
using Skarbiec.Portfolio.Features.RecurringIncomes.Occurrences.UndoOccurrence;
using Skarbiec.Portfolio.Features.RemoveAsset;
using Skarbiec.Portfolio.Features.RestoreAsset;
using Skarbiec.Portfolio.Features.Transfers.CreateTransfer;
using Skarbiec.Portfolio.Features.Transfers.DeleteTransfer;
using Skarbiec.Portfolio.Features.UpdateAsset;
using Skarbiec.Portfolio.Features.UpdateTransaction;
using Skarbiec.Portfolio.Features.Watchlist.AddWatchlistItem;
using Skarbiec.Portfolio.Features.Watchlist.RemoveWatchlistItem;
using Skarbiec.Portfolio.Features.Watchlist.UpdateWatchlistItem;
using Skarbiec.Portfolio.MarketData;
using Skarbiec.ServiceDefaults.Authentication;
using Skarbiec.Testing.Containers;
using Skarbiec.Testing.Messaging;

namespace Skarbiec.Portfolio.Tests.Fixtures;

// Builds its own provider so no hosted service starts and the outbox row is never delivered before the assertion reads it.
public abstract class PortfolioOutboxTestBase(SkarbiecContainersFixture containers) : IAsyncLifetime, IClassFixture<SkarbiecContainersFixture>
{
    protected static readonly Guid UserId = Guid.NewGuid();

    private StubCurrentUser CurrentUser { get; } = new(UserId);

    private protected SaveChangesCounter SaveChanges { get; } = new();

    private protected TransactionReadCounter TransactionReads { get; } = new();

    protected ServiceProvider Provider { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Provider = HostlessOutboxProvider.Build<PortfolioDbContext>(containers, services =>
        {
            services.ConfigureDbContext<PortfolioDbContext>(options => options.AddInterceptors(SaveChanges, TransactionReads));
            services.AddSingleton<ICurrentUser>(CurrentUser);
            services.AddSingleton<IInstrumentLookupClient>(new FakeInstrumentLookupClient());
            services.AddSingleton<IFxRateLookupClient>(new FakeFxRateLookupClient());
            services.AddSingleton<IBondRateLookupClient>(new FakeBondRateLookupClient());
            services.AddSingleton<IInstrumentQuoteLookupClient>(new FakeInstrumentQuoteLookupClient());
            services.AddSingleton(TimeProvider.System);
            services.AddScoped<PositionEventPublisher>();
            services.AddScoped<CreateAccountHandler>();
            services.AddScoped<UpdateAccountHandler>();
            services.AddScoped<AddAssetHandler>();
            services.AddScoped<UpdateAssetHandler>();
            services.AddScoped<RecordTransactionHandler>();
            services.AddScoped<UpdateTransactionHandler>();
            services.AddScoped<DeleteTransactionHandler>();
            services.AddScoped<RemoveAssetHandler>();
            services.AddScoped<AddDepositHandler>();
            services.AddScoped<AddBondHandler>();
            services.AddScoped<UpdateBondHandler>();
            services.AddScoped<SettleBondInterestHandler>();
            services.AddScoped<UndoBondInterestSettlementHandler>();
            services.AddScoped<RedeemBondHandler>();
            services.AddScoped<RedeemBondEarlyHandler>();
            services.AddScoped<SwapBondHandler>();
            services.AddScoped<AddMetalHandler>();
            services.AddScoped<UpdateMetalHandler>();
            services.AddScoped<SettleBankInterestHandler>();
            services.AddScoped<UndoBankInterestSettlementHandler>();
            services.AddScoped<UpdateDepositHandler>();
            services.AddScoped<SettleDepositHandler>();
            services.AddScoped<PayOutDepositHandler>();
            services.AddScoped<RollOverDepositHandler>();
            services.AddScoped<ArchiveAccountHandler>();
            services.AddScoped<RestoreAccountHandler>();
            services.AddScoped<ArchiveAssetHandler>();
            services.AddScoped<RestoreAssetHandler>();
            services.AddScoped<DeleteAccountHandler>();
            services.AddScoped<CreateCashPlaceHandler>();
            services.AddScoped<UpdateCashPlaceHandler>();
            services.AddScoped<DeleteCashPlaceHandler>();
            services.AddScoped<CreateAssetGroupHandler>();
            services.AddScoped<UpdateAssetGroupHandler>();
            services.AddScoped<DeleteAssetGroupHandler>();
            services.AddScoped<AssignAssetsToGroupHandler>();
            services.AddScoped<CreateRecurringIncomeHandler>();
            services.AddScoped<SettleOccurrenceHandler>();
            services.AddScoped<UndoOccurrenceHandler>();
            services.AddScoped<CreateTransferHandler>();
            services.AddScoped<DeleteTransferHandler>();
            services.AddScoped<AddWatchlistItemHandler>();
            services.AddScoped<UpdateWatchlistItemHandler>();
            services.AddScoped<RemoveWatchlistItemHandler>();
        });

        await using (var scope = Provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PortfolioDbContext>().Database.MigrateAsync();
        }

        await containers.ResetDatabaseAsync();
    }

    public async ValueTask DisposeAsync() => await Provider.DisposeAsync();

    protected void ActAs(Guid userId) => CurrentUser.UserId = userId;

    protected async Task<int> CountPositionEventsAsync(CancellationToken cancellationToken)
    {
        await using var scope = Provider.CreateAsyncScope();

        return (await scope.ServiceProvider.GetRequiredService<PortfolioDbContext>()
            .ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Count;
    }

    protected static async Task<Guid> CreateAccountAsync(
        IServiceProvider services,
        string name,
        CancellationToken cancellationToken,
        AccountType type = AccountType.Brokerage,
        TaxWrapper wrapper = TaxWrapper.Regular)
    {
        var result = await services.GetRequiredService<CreateAccountHandler>()
            .HandleAsync(new CreateAccountRequest { Name = name, Type = type, Wrapper = wrapper }, cancellationToken);
        Assert.True(result.IsSuccess);

        return result.Value.Id;
    }

    protected static async Task<Guid> CreateGroupAsync(IServiceProvider services, string name, CancellationToken cancellationToken)
    {
        var usedColors = await services.GetRequiredService<PortfolioDbContext>().AssetGroups.CountAsync(cancellationToken);
        var created = await services.GetRequiredService<CreateAssetGroupHandler>().HandleAsync(
            new CreateAssetGroupRequest { Name = name, Color = AssetGroupColors.Keys[usedColors] }, cancellationToken);
        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.Code : null);

        return created.Value.Id;
    }

    protected sealed record CreatedBank(Guid Id, Guid CashAssetId);

    protected static async Task<CreatedBank> CreateBankAccountAsync(
        IServiceProvider services,
        string name,
        string currency,
        decimal annualInterestRatePercent,
        CancellationToken cancellationToken,
        decimal balance = 0m,
        Guid? groupId = null)
    {
        var created = await services.GetRequiredService<CreateAccountHandler>().HandleAsync(
            new CreateAccountRequest
            {
                Name = name,
                Type = AccountType.Bank,
                Wrapper = TaxWrapper.Regular,
                Currency = currency,
                Variant = BankVariant.Savings,
                BankName = name,
                AnnualInterestRatePercent = annualInterestRatePercent,
                GroupId = groupId
            },
            cancellationToken);
        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.Code : null);

        if (balance > 0m)
        {
            var deposited = await services.GetRequiredService<RecordTransactionHandler>().HandleAsync(
                created.Value.CashAssetId,
                new RecordTransactionRequest { Type = TransactionType.Deposit, Quantity = balance, UnitPrice = 1m, Date = PortfolioApi.DefaultTopUpDate },
                cancellationToken);
            Assert.True(deposited.IsSuccess, deposited.IsFailure ? deposited.Error.Code : null);
        }

        return new CreatedBank(created.Value.Id, created.Value.CashAssetId);
    }

    protected sealed record OpenedDeposit(Guid AssetId, Guid SourceAccountId, Guid SourceMoneyAssetId);

    // Opens a deposit from a fresh PLN bank account holding far more than the deposit needs.
    protected static async Task<OpenedDeposit> OpenDepositAsync(
        IServiceProvider services,
        CancellationToken cancellationToken,
        AddDepositRequest? request = null,
        decimal sourceBalance = 1_000_000m)
    {
        var source = await CreateBankAccountAsync(
            services, $"Funding {Guid.NewGuid():N}", "PLN", 0m, cancellationToken, balance: sourceBalance);
        var added = await services.GetRequiredService<AddDepositHandler>().HandleAsync(
            (request ?? PortfolioApi.NewDepositRequest()) with { SourceAccountId = source.Id }, cancellationToken);
        Assert.True(added.IsSuccess, added.IsFailure ? added.Error.Code : null);

        return new OpenedDeposit(added.Value.AssetId, source.Id, source.CashAssetId);
    }

    protected static async Task<Guid> AddCashWithBalanceAsync(
        IServiceProvider services, decimal balance, CancellationToken cancellationToken)
    {
        var placeResult = await services.GetRequiredService<CreateCashPlaceHandler>().HandleAsync(
            new CreateCashPlaceRequest { Name = "Cash account", Currencies = ["PLN"] },
            cancellationToken);
        Assert.True(placeResult.IsSuccess, placeResult.IsFailure ? placeResult.Error.Code : null);
        var balanceId = await services.GetRequiredService<PortfolioDbContext>().Assets
            .Where(a => a.CashPlaceId == placeResult.Value.Id)
            .Select(a => a.Id)
            .SingleAsync(cancellationToken);

        var topUp = await services.GetRequiredService<RecordTransactionHandler>().HandleAsync(
            balanceId,
            new RecordTransactionRequest { Type = TransactionType.Deposit, Quantity = balance, UnitPrice = 1m, Date = PortfolioApi.DefaultTopUpDate },
            cancellationToken);
        Assert.True(topUp.IsSuccess, topUp.IsFailure ? topUp.Error.Code : null);

        return balanceId;
    }

    protected static async Task<Guid> AddStockAsync(
        IServiceProvider services, Guid accountId, string name, CancellationToken cancellationToken)
    {
        var assetResult = await services.GetRequiredService<AddAssetHandler>().HandleAsync(
            new AddAssetRequest { AccountId = accountId, AssetClass = AssetClass.Stock, Name = name, Currency = "PLN", InstrumentId = FakeInstrumentLookupClient.IdFor(AssetClass.Stock, "PLN") },
            cancellationToken);
        Assert.True(assetResult.IsSuccess, assetResult.IsFailure ? assetResult.Error.Code : null);

        return assetResult.Value.Id;
    }

    // Covers any trade a test records: every stock trade settles in the account's cash.
    protected static async Task FundAccountCashAsync(
        IServiceProvider services, Guid accountId, CancellationToken cancellationToken, decimal amount = 1_000_000m)
    {
        var cash = await LoadAccountCashAsync(services, accountId, cancellationToken);

        var funded = await services.GetRequiredService<RecordTransactionHandler>().HandleAsync(
            cash.Id,
            new RecordTransactionRequest { Type = TransactionType.Deposit, Quantity = amount, UnitPrice = 1m, Date = AccountsApi.FundingDay },
            cancellationToken);
        Assert.True(funded.IsSuccess, funded.IsFailure ? funded.Error.Code : null);
    }

    // An account archives only with its cash at zero.
    protected static async Task DrainAccountCashAsync(
        IServiceProvider services, Guid accountId, CancellationToken cancellationToken)
    {
        var cash = await LoadAccountCashAsync(services, accountId, cancellationToken);

        var drained = await services.GetRequiredService<RecordTransactionHandler>().HandleAsync(
            cash.Id,
            new RecordTransactionRequest { Type = TransactionType.Withdraw, Quantity = cash.Quantity, UnitPrice = 1m, Date = AccountsApi.DrainDay },
            cancellationToken);
        Assert.True(drained.IsSuccess, drained.IsFailure ? drained.Error.Code : null);
    }

    protected static Task<Asset> LoadAccountCashAsync(IServiceProvider services, Guid accountId, CancellationToken cancellationToken) =>
        services.GetRequiredService<PortfolioDbContext>().Assets
            .AsNoTracking()
            .SingleAsync(a => a.AccountId == accountId && a.AssetClass == AssetClass.Cash, cancellationToken);

    protected static async Task<Guid> AddAssetWithBuyAsync(
        IServiceProvider services, Guid accountId, string name, CancellationToken cancellationToken)
    {
        await FundAccountCashAsync(services, accountId, cancellationToken);
        var assetResult = await services.GetRequiredService<AddAssetHandler>().HandleAsync(
            new AddAssetRequest { AccountId = accountId, AssetClass = AssetClass.Stock, Name = name, Currency = "PLN", InstrumentId = FakeInstrumentLookupClient.IdFor(AssetClass.Stock, "PLN") },
            cancellationToken);
        Assert.True(assetResult.IsSuccess);

        var buyResult = await services.GetRequiredService<RecordTransactionHandler>().HandleAsync(
            assetResult.Value.Id,
            new RecordTransactionRequest { Type = TransactionType.Buy, Quantity = 5m, UnitPrice = 10m, Date = new DateOnly(2026, 1, 2) },
            cancellationToken);
        Assert.True(buyResult.IsSuccess);

        return assetResult.Value.Id;
    }

    protected static async Task<(Guid AssetId, IReadOnlyList<Guid> TransactionIds)> AddStockWithTransactionsAsync(
        IServiceProvider services,
        Guid accountId,
        string name,
        IReadOnlyList<(TransactionType Type, decimal Quantity, DateOnly Date)> transactions,
        CancellationToken cancellationToken)
    {
        await FundAccountCashAsync(services, accountId, cancellationToken);
        var assetResult = await services.GetRequiredService<AddAssetHandler>().HandleAsync(
            new AddAssetRequest { AccountId = accountId, AssetClass = AssetClass.Stock, Name = name, Currency = "PLN", InstrumentId = FakeInstrumentLookupClient.IdFor(AssetClass.Stock, "PLN") },
            cancellationToken);
        Assert.True(assetResult.IsSuccess, assetResult.IsFailure ? assetResult.Error.Code : null);

        var recordHandler = services.GetRequiredService<RecordTransactionHandler>();
        var transactionIds = new List<Guid>();
        foreach (var (type, quantity, date) in transactions)
        {
            var recorded = await recordHandler.HandleAsync(
                assetResult.Value.Id,
                new RecordTransactionRequest { Type = type, Quantity = quantity, UnitPrice = 10m, Date = date },
                cancellationToken);
            Assert.True(recorded.IsSuccess, recorded.IsFailure ? recorded.Error.Code : null);
            transactionIds.Add(recorded.Value.Id);
        }

        return (assetResult.Value.Id, transactionIds);
    }

    protected static async Task<Guid> AddSecurityAsync(
        IServiceProvider services, Guid accountId, AssetClass assetClass, string name, CancellationToken cancellationToken)
    {
        var added = await services.GetRequiredService<AddAssetHandler>().HandleAsync(
            new AddAssetRequest { AccountId = accountId, AssetClass = assetClass, Name = name, Currency = "PLN", InstrumentId = FakeInstrumentLookupClient.IdFor(assetClass, "PLN") },
            cancellationToken);
        Assert.True(added.IsSuccess, added.IsFailure ? added.Error.Code : null);

        return added.Value.Id;
    }

    protected static async Task<Guid> RecordTradeAsync(
        IServiceProvider services,
        Guid assetId,
        TransactionType type,
        decimal quantity,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var recorded = await services.GetRequiredService<RecordTransactionHandler>().HandleAsync(
            assetId,
            new RecordTransactionRequest { Type = type, Quantity = quantity, UnitPrice = 10m, Date = date },
            cancellationToken);
        Assert.True(recorded.IsSuccess, recorded.IsFailure ? recorded.Error.Code : null);

        return recorded.Value.Id;
    }
}
