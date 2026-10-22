using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.Accounts.ArchiveAccount;
using Skarbiec.Portfolio.Features.Accounts.BankInterest.GetBankInterestPreview;
using Skarbiec.Portfolio.Features.Accounts.BankInterest.SettleBankInterest;
using Skarbiec.Portfolio.Features.Accounts.BankInterest.UndoBankInterestSettlement;
using Skarbiec.Portfolio.Features.Accounts.CreateAccount;
using Skarbiec.Portfolio.Features.Accounts.DeleteAccount;
using Skarbiec.Portfolio.Features.Accounts.DeleteContributionLimit;
using Skarbiec.Portfolio.Features.Accounts.GetAccount;
using Skarbiec.Portfolio.Features.Accounts.ListAccounts;
using Skarbiec.Portfolio.Features.Accounts.RestoreAccount;
using Skarbiec.Portfolio.Features.Accounts.SetContributionLimit;
using Skarbiec.Portfolio.Features.Accounts.UpdateAccount;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.ArchiveAsset;
using Skarbiec.Portfolio.Features.AssetGroups.AssignAssetsToGroup;
using Skarbiec.Portfolio.Features.AssetGroups.CreateAssetGroup;
using Skarbiec.Portfolio.Features.AssetGroups.DeleteAssetGroup;
using Skarbiec.Portfolio.Features.AssetGroups.ListAssetGroups;
using Skarbiec.Portfolio.Features.AssetGroups.ListAssignableAssets;
using Skarbiec.Portfolio.Features.AssetGroups.UpdateAssetGroup;
using Skarbiec.Portfolio.Features.Bonds.AddBond;
using Skarbiec.Portfolio.Features.Bonds.GetBond;
using Skarbiec.Portfolio.Features.Bonds.GetBondEarlyRedemptionPreview;
using Skarbiec.Portfolio.Features.Bonds.GetBondRedemptionPreview;
using Skarbiec.Portfolio.Features.Bonds.ListBonds;
using Skarbiec.Portfolio.Features.Bonds.PreviewBondInterest;
using Skarbiec.Portfolio.Features.Bonds.RedeemBond;
using Skarbiec.Portfolio.Features.Bonds.RedeemBondEarly;
using Skarbiec.Portfolio.Features.Bonds.SettleBondInterest;
using Skarbiec.Portfolio.Features.Bonds.SwapBond;
using Skarbiec.Portfolio.Features.Bonds.UndoBondInterestSettlement;
using Skarbiec.Portfolio.Features.Bonds.UpdateBond;
using Skarbiec.Portfolio.Features.CashPlaces.AddCashPlaceCurrency;
using Skarbiec.Portfolio.Features.CashPlaces.ArchiveCashPlace;
using Skarbiec.Portfolio.Features.CashPlaces.CreateCashPlace;
using Skarbiec.Portfolio.Features.CashPlaces.DeleteCashPlace;
using Skarbiec.Portfolio.Features.CashPlaces.GetCashPlace;
using Skarbiec.Portfolio.Features.CashPlaces.ListCashPlaces;
using Skarbiec.Portfolio.Features.CashPlaces.RestoreCashPlace;
using Skarbiec.Portfolio.Features.CashPlaces.UpdateCashPlace;
using Skarbiec.Portfolio.Features.DeleteTransaction;
using Skarbiec.Portfolio.Features.Deposits.AddDeposit;
using Skarbiec.Portfolio.Features.Deposits.GetDeposit;
using Skarbiec.Portfolio.Features.Deposits.GetSettlementPreview;
using Skarbiec.Portfolio.Features.Deposits.ListDeposits;
using Skarbiec.Portfolio.Features.Deposits.PayOutDeposit;
using Skarbiec.Portfolio.Features.Deposits.RollOverDeposit;
using Skarbiec.Portfolio.Features.Deposits.SettleDeposit;
using Skarbiec.Portfolio.Features.Deposits.UpdateDeposit;
using Skarbiec.Portfolio.Features.GetAsset;
using Skarbiec.Portfolio.Features.ListAssets;
using Skarbiec.Portfolio.Features.ListTransactions;
using Skarbiec.Portfolio.Features.Planning.GetOverdueOccurrences;
using Skarbiec.Portfolio.Features.Planning.GetPlanningMonth;
using Skarbiec.Portfolio.Features.Planning.GetPlanningYear;
using Skarbiec.Portfolio.Features.OneOffExpenses.CreateOneOffExpense;
using Skarbiec.Portfolio.Features.OneOffExpenses.DeleteOneOffExpense;
using Skarbiec.Portfolio.Features.OneOffExpenses.UpdateOneOffExpense;
using Skarbiec.Portfolio.Features.OneOffIncomes.CreateOneOffIncome;
using Skarbiec.Portfolio.Features.OneOffIncomes.DeleteOneOffIncome;
using Skarbiec.Portfolio.Features.OneOffIncomes.SettleOneOffIncome;
using Skarbiec.Portfolio.Features.OneOffIncomes.UndoOneOffIncomeSettlement;
using Skarbiec.Portfolio.Features.OneOffIncomes.UpdateOneOffIncome;
using Skarbiec.Portfolio.Features.Planning.GetPlanningSettings;
using Skarbiec.Portfolio.Features.Planning.GetPlanningTodo;
using Skarbiec.Portfolio.Features.Planning.SavePlanningSettings;
using Skarbiec.Portfolio.Features.RecordTransaction;
using Skarbiec.Portfolio.Features.RecurringExpenses.CreateRecurringExpense;
using Skarbiec.Portfolio.Features.RecurringExpenses.DeleteRecurringExpense;
using Skarbiec.Portfolio.Features.RecurringExpenses.DeleteRecurringExpenseChange;
using Skarbiec.Portfolio.Features.RecurringExpenses.EndRecurringExpense;
using Skarbiec.Portfolio.Features.RecurringExpenses.UpdateRecurringExpense;
using Skarbiec.Portfolio.Features.RecurringIncomes.CreateRecurringIncome;
using Skarbiec.Portfolio.Features.RecurringIncomes.DeleteRecurringIncome;
using Skarbiec.Portfolio.Features.RecurringIncomes.DeleteRecurringIncomeChange;
using Skarbiec.Portfolio.Features.RecurringIncomes.EndRecurringIncome;
using Skarbiec.Portfolio.Features.RecurringIncomes.Occurrences.SettleOccurrence;
using Skarbiec.Portfolio.Features.RecurringIncomes.Occurrences.SkipOccurrence;
using Skarbiec.Portfolio.Features.RecurringIncomes.Occurrences.UndoOccurrence;
using Skarbiec.Portfolio.Features.RecurringIncomes.UpdateRecurringIncome;
using Skarbiec.Portfolio.Features.RemoveAsset;
using Skarbiec.Portfolio.Features.RestoreAsset;
using Skarbiec.Portfolio.Features.Metals.AddMetal;
using Skarbiec.Portfolio.Features.Metals.GetMetal;
using Skarbiec.Portfolio.Features.Metals.ListMetals;
using Skarbiec.Portfolio.Features.Metals.UpdateMetal;
using Skarbiec.Portfolio.Features.Securities.ListSecurities;
using Skarbiec.Portfolio.Features.Transfers.CreateTransfer;
using Skarbiec.Portfolio.Features.Transfers.DeleteTransfer;
using Skarbiec.Portfolio.Features.Transfers.ListTransferCandidates;
using Skarbiec.Portfolio.Features.UpdateAsset;
using Skarbiec.Portfolio.Features.UpdateTransaction;
using Skarbiec.Portfolio.Features.Watchlist.AddWatchlistItem;
using Skarbiec.Portfolio.Features.Watchlist.GetWatchlistItem;
using Skarbiec.Portfolio.Features.Watchlist.ListWatchlist;
using Skarbiec.Portfolio.Features.Watchlist.RemoveWatchlistItem;
using Skarbiec.Portfolio.Features.Watchlist.UpdateWatchlistItem;
using Skarbiec.Portfolio.MarketData;
using Skarbiec.Portfolio.Messaging;
using Skarbiec.ServiceDefaults.Authentication;
using Skarbiec.ServiceDefaults.Messaging;
using Skarbiec.ServiceDefaults.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddServiceOpenApi();

if (!OpenApiBuildTime.IsActive)
{
    // Not AddNpgsqlDbContext: it always pools, and a pooled context cannot take the request-scoped ICurrentUser.
    var portfolioConnectionString = builder.Configuration.GetConnectionString("portfolio-db")
        ?? throw new InvalidOperationException("Missing connection string 'portfolio-db'.");
    builder.Services.AddDbContext<PortfolioDbContext>(options => options.UseNpgsql(portfolioConnectionString));

    builder.AddRabbitMqMessaging<WebApplicationBuilder, PortfolioDbContext>(configureConsumers: x =>
    {
        x.AddConsumer<LatestValuationChangedConsumer>(typeof(LatestValuationChangedConsumerDefinition));
    });

    builder.Services.AddHttpClient<IInstrumentLookupClient, MarketDataInstrumentLookupClient>(client =>
    {
        client.BaseAddress = new Uri("https+http://marketdata-service");
    });

    builder.Services.AddHttpClient<IFxRateLookupClient, MarketDataFxRateLookupClient>(client =>
    {
        client.BaseAddress = new Uri("https+http://marketdata-service");
    });

    builder.Services.AddHttpClient<IBondRateLookupClient, MarketDataBondRateLookupClient>(client =>
    {
        client.BaseAddress = new Uri("https+http://marketdata-service");
    });

    builder.Services.AddHttpClient<IInstrumentQuoteLookupClient, MarketDataInstrumentQuoteLookupClient>(client =>
    {
        client.BaseAddress = new Uri("https+http://marketdata-service");
    });
}

builder.Services.AddValidation();

// Registered so a test can swap in a fake clock and pin an event's OccurredAtUtc.
builder.Services.TryAddSingleton(TimeProvider.System);

builder.Services.AddScoped<PositionEventPublisher>();
builder.Services.AddScoped<CreateAccountHandler>();
builder.Services.AddScoped<UpdateAccountHandler>();
builder.Services.AddScoped<ListAccountsHandler>();
builder.Services.AddScoped<GetAccountHandler>();
builder.Services.AddScoped<DeleteAccountHandler>();
builder.Services.AddScoped<ArchiveAccountHandler>();
builder.Services.AddScoped<RestoreAccountHandler>();
builder.Services.AddScoped<SetContributionLimitHandler>();
builder.Services.AddScoped<DeleteContributionLimitHandler>();
builder.Services.AddScoped<AddAssetHandler>();
builder.Services.AddScoped<UpdateAssetHandler>();
builder.Services.AddScoped<ListAssetsHandler>();
builder.Services.AddScoped<GetAssetHandler>();
builder.Services.AddScoped<RemoveAssetHandler>();
builder.Services.AddScoped<ArchiveAssetHandler>();
builder.Services.AddScoped<RestoreAssetHandler>();
builder.Services.AddScoped<RecordTransactionHandler>();
builder.Services.AddScoped<ListTransactionsHandler>();
builder.Services.AddScoped<UpdateTransactionHandler>();
builder.Services.AddScoped<DeleteTransactionHandler>();
builder.Services.AddScoped<AddDepositHandler>();
builder.Services.AddScoped<UpdateDepositHandler>();
builder.Services.AddScoped<GetDepositHandler>();
builder.Services.AddScoped<ListDepositsHandler>();
builder.Services.AddScoped<GetSettlementPreviewHandler>();
builder.Services.AddScoped<SettleDepositHandler>();
builder.Services.AddScoped<PayOutDepositHandler>();
builder.Services.AddScoped<RollOverDepositHandler>();
builder.Services.AddScoped<AddBondHandler>();
builder.Services.AddScoped<UpdateBondHandler>();
builder.Services.AddScoped<GetBondHandler>();
builder.Services.AddScoped<ListBondsHandler>();
builder.Services.AddScoped<SettleBondInterestHandler>();
builder.Services.AddScoped<PreviewBondInterestHandler>();
builder.Services.AddScoped<UndoBondInterestSettlementHandler>();
builder.Services.AddScoped<RedeemBondHandler>();
builder.Services.AddScoped<GetBondRedemptionPreviewHandler>();
builder.Services.AddScoped<SwapBondHandler>();
builder.Services.AddScoped<RedeemBondEarlyHandler>();
builder.Services.AddScoped<GetBondEarlyRedemptionPreviewHandler>();
builder.Services.AddScoped<AddMetalHandler>();
builder.Services.AddScoped<UpdateMetalHandler>();
builder.Services.AddScoped<GetMetalHandler>();
builder.Services.AddScoped<ListMetalsHandler>();
builder.Services.AddScoped<CreateAssetGroupHandler>();
builder.Services.AddScoped<ListAssetGroupsHandler>();
builder.Services.AddScoped<UpdateAssetGroupHandler>();
builder.Services.AddScoped<DeleteAssetGroupHandler>();
builder.Services.AddScoped<AssignAssetsToGroupHandler>();
builder.Services.AddScoped<ListAssignableAssetsHandler>();
builder.Services.AddScoped<CreateCashPlaceHandler>();
builder.Services.AddScoped<AddCashPlaceCurrencyHandler>();
builder.Services.AddScoped<ListCashPlacesHandler>();
builder.Services.AddScoped<GetCashPlaceHandler>();
builder.Services.AddScoped<UpdateCashPlaceHandler>();
builder.Services.AddScoped<ArchiveCashPlaceHandler>();
builder.Services.AddScoped<RestoreCashPlaceHandler>();
builder.Services.AddScoped<DeleteCashPlaceHandler>();
builder.Services.AddScoped<CreateOneOffIncomeHandler>();
builder.Services.AddScoped<UpdateOneOffIncomeHandler>();
builder.Services.AddScoped<DeleteOneOffIncomeHandler>();
builder.Services.AddScoped<SettleOneOffIncomeHandler>();
builder.Services.AddScoped<UndoOneOffIncomeSettlementHandler>();
builder.Services.AddScoped<CreateOneOffExpenseHandler>();
builder.Services.AddScoped<UpdateOneOffExpenseHandler>();
builder.Services.AddScoped<DeleteOneOffExpenseHandler>();
builder.Services.AddScoped<CreateRecurringIncomeHandler>();
builder.Services.AddScoped<EndRecurringIncomeHandler>();
builder.Services.AddScoped<DeleteRecurringIncomeHandler>();
builder.Services.AddScoped<UpdateRecurringIncomeHandler>();
builder.Services.AddScoped<DeleteRecurringIncomeChangeHandler>();
builder.Services.AddScoped<CreateRecurringExpenseHandler>();
builder.Services.AddScoped<EndRecurringExpenseHandler>();
builder.Services.AddScoped<DeleteRecurringExpenseHandler>();
builder.Services.AddScoped<UpdateRecurringExpenseHandler>();
builder.Services.AddScoped<DeleteRecurringExpenseChangeHandler>();
builder.Services.AddScoped<SavePlanningSettingsHandler>();
builder.Services.AddScoped<GetPlanningSettingsHandler>();
builder.Services.AddScoped<SettleOccurrenceHandler>();
builder.Services.AddScoped<SkipOccurrenceHandler>();
builder.Services.AddScoped<UndoOccurrenceHandler>();
builder.Services.AddScoped<PlanningMonthLoader>();
builder.Services.AddScoped<GetPlanningMonthHandler>();
builder.Services.AddScoped<GetPlanningYearHandler>();
builder.Services.AddScoped<GetPlanningTodoHandler>();
builder.Services.AddScoped<GetOverdueOccurrencesHandler>();
builder.Services.AddScoped<ListSecuritiesHandler>();
builder.Services.AddScoped<GetBankInterestPreviewHandler>();
builder.Services.AddScoped<SettleBankInterestHandler>();
builder.Services.AddScoped<UndoBankInterestSettlementHandler>();
builder.Services.AddScoped<ListTransferCandidatesHandler>();
builder.Services.AddScoped<CreateTransferHandler>();
builder.Services.AddScoped<DeleteTransferHandler>();
builder.Services.AddScoped<AddWatchlistItemHandler>();
builder.Services.AddScoped<ListWatchlistHandler>();
builder.Services.AddScoped<GetWatchlistItemHandler>();
builder.Services.AddScoped<UpdateWatchlistItemHandler>();
builder.Services.AddScoped<RemoveWatchlistItemHandler>();

var app = builder.Build();

app.UseServiceDefaults();
app.MapDefaultEndpoints();
app.MapServiceOpenApi("portfolio");

app.MapCreateAccountEndpoint();
app.MapUpdateAccountEndpoint();
app.MapListAccountsEndpoint();
app.MapGetAccountEndpoint();
app.MapDeleteAccountEndpoint();
app.MapArchiveAccountEndpoint();
app.MapRestoreAccountEndpoint();
app.MapSetContributionLimitEndpoint();
app.MapDeleteContributionLimitEndpoint();
app.MapAddAssetEndpoint();
app.MapUpdateAssetEndpoint();
app.MapListAssetsEndpoint();
app.MapGetAssetEndpoint();
app.MapRemoveAssetEndpoint();
app.MapArchiveAssetEndpoint();
app.MapRestoreAssetEndpoint();
app.MapRecordTransactionEndpoint();
app.MapListTransactionsEndpoint();
app.MapUpdateTransactionEndpoint();
app.MapDeleteTransactionEndpoint();
app.MapAddDepositEndpoint();
app.MapUpdateDepositEndpoint();
app.MapGetDepositEndpoint();
app.MapListDepositsEndpoint();
app.MapGetSettlementPreviewEndpoint();
app.MapSettleDepositEndpoint();
app.MapPayOutDepositEndpoint();
app.MapRollOverDepositEndpoint();
app.MapAddBondEndpoint();
app.MapUpdateBondEndpoint();
app.MapGetBondEndpoint();
app.MapListBondsEndpoint();
app.MapSettleBondInterestEndpoint();
app.MapPreviewBondInterestEndpoint();
app.MapUndoBondInterestSettlementEndpoint();
app.MapRedeemBondEndpoint();
app.MapGetBondRedemptionPreviewEndpoint();
app.MapSwapBondEndpoint();
app.MapRedeemBondEarlyEndpoint();
app.MapGetBondEarlyRedemptionPreviewEndpoint();
app.MapAddMetalEndpoint();
app.MapUpdateMetalEndpoint();
app.MapGetMetalEndpoint();
app.MapListMetalsEndpoint();
app.MapCreateAssetGroupEndpoint();
app.MapListAssetGroupsEndpoint();
app.MapUpdateAssetGroupEndpoint();
app.MapDeleteAssetGroupEndpoint();
app.MapAssignAssetsToGroupEndpoint();
app.MapListAssignableAssetsEndpoint();
app.MapCreateCashPlaceEndpoint();
app.MapAddCashPlaceCurrencyEndpoint();
app.MapListCashPlacesEndpoint();
app.MapGetCashPlaceEndpoint();
app.MapUpdateCashPlaceEndpoint();
app.MapArchiveCashPlaceEndpoint();
app.MapRestoreCashPlaceEndpoint();
app.MapDeleteCashPlaceEndpoint();
app.MapCreateOneOffIncomeEndpoint();
app.MapUpdateOneOffIncomeEndpoint();
app.MapDeleteOneOffIncomeEndpoint();
app.MapSettleOneOffIncomeEndpoint();
app.MapUndoOneOffIncomeSettlementEndpoint();
app.MapCreateOneOffExpenseEndpoint();
app.MapUpdateOneOffExpenseEndpoint();
app.MapDeleteOneOffExpenseEndpoint();
app.MapCreateRecurringIncomeEndpoint();
app.MapEndRecurringIncomeEndpoint();
app.MapDeleteRecurringIncomeEndpoint();
app.MapUpdateRecurringIncomeEndpoint();
app.MapDeleteRecurringIncomeChangeEndpoint();
app.MapCreateRecurringExpenseEndpoint();
app.MapEndRecurringExpenseEndpoint();
app.MapDeleteRecurringExpenseEndpoint();
app.MapUpdateRecurringExpenseEndpoint();
app.MapDeleteRecurringExpenseChangeEndpoint();
app.MapSettleOccurrenceEndpoint();
app.MapSkipOccurrenceEndpoint();
app.MapUndoOccurrenceEndpoint();
app.MapGetPlanningMonthEndpoint();
app.MapGetPlanningYearEndpoint();
app.MapSavePlanningSettingsEndpoint();
app.MapGetPlanningSettingsEndpoint();
app.MapGetPlanningTodoEndpoint();
app.MapGetOverdueOccurrencesEndpoint();
app.MapListSecuritiesEndpoint();
app.MapGetBankInterestPreviewEndpoint();
app.MapSettleBankInterestEndpoint();
app.MapUndoBankInterestSettlementEndpoint();
app.MapListTransferCandidatesEndpoint();
app.MapCreateTransferEndpoint();
app.MapDeleteTransferEndpoint();
app.MapAddWatchlistItemEndpoint();
app.MapListWatchlistEndpoint();
app.MapGetWatchlistItemEndpoint();
app.MapUpdateWatchlistItemEndpoint();
app.MapRemoveWatchlistItemEndpoint();

// Diagnostic endpoint proving a Gateway-forwarded JWT authorizes a call routed to this service.
app.MapGet("/api/portfolio/me", (ICurrentUser currentUser) => TypedResults.Ok(currentUser.UserId))
    .RequireAuthorization();

// Production applies migrations as an explicit deploy step instead (see deploy/README.md).
if (!OpenApiBuildTime.IsActive && app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<PortfolioDbContext>().Database.MigrateAsync();
}

app.Run();

// Exposed for Skarbiec.Portfolio.Tests' WebApplicationFactory<Program>.
public partial class Program;
