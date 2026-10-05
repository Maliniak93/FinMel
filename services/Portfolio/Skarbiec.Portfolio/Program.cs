using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.ArchiveAsset;
using Skarbiec.Portfolio.Features.ArchivePortfolio;
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
using Skarbiec.Portfolio.Features.CreatePortfolio;
using Skarbiec.Portfolio.Features.DeletePortfolio;
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
using Skarbiec.Portfolio.Features.GetPortfolio;
using Skarbiec.Portfolio.Features.ListAssets;
using Skarbiec.Portfolio.Features.ListPortfolios;
using Skarbiec.Portfolio.Features.ListTransactions;
using Skarbiec.Portfolio.Features.RecordTransaction;
using Skarbiec.Portfolio.Features.RemoveAsset;
using Skarbiec.Portfolio.Features.RestoreAsset;
using Skarbiec.Portfolio.Features.RestorePortfolio;
using Skarbiec.Portfolio.Features.SavingsAccounts.AddSavingsAccount;
using Skarbiec.Portfolio.Features.SavingsAccounts.GetSavingsAccount;
using Skarbiec.Portfolio.Features.SavingsAccounts.GetSavingsInterestPreview;
using Skarbiec.Portfolio.Features.SavingsAccounts.ListSavingsAccounts;
using Skarbiec.Portfolio.Features.SavingsAccounts.SettleSavingsInterest;
using Skarbiec.Portfolio.Features.SavingsAccounts.UndoSavingsInterestSettlement;
using Skarbiec.Portfolio.Features.SavingsAccounts.UpdateSavingsAccount;
using Skarbiec.Portfolio.Features.Transfers.CreateTransfer;
using Skarbiec.Portfolio.Features.Transfers.DeleteTransfer;
using Skarbiec.Portfolio.Features.Transfers.ListTransferCandidates;
using Skarbiec.Portfolio.Features.UpdateAsset;
using Skarbiec.Portfolio.Features.UpdatePortfolio;
using Skarbiec.Portfolio.Features.UpdateTransaction;
using Skarbiec.Portfolio.MarketData;
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

    builder.AddRabbitMqMessaging<WebApplicationBuilder, PortfolioDbContext>();

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
}

builder.Services.AddValidation();

// Registered so a test can swap in a fake clock and pin an event's OccurredAtUtc.
builder.Services.TryAddSingleton(TimeProvider.System);

builder.Services.AddScoped<PositionEventPublisher>();
builder.Services.AddScoped<CreatePortfolioHandler>();
builder.Services.AddScoped<UpdatePortfolioHandler>();
builder.Services.AddScoped<ListPortfoliosHandler>();
builder.Services.AddScoped<GetPortfolioHandler>();
builder.Services.AddScoped<DeletePortfolioHandler>();
builder.Services.AddScoped<ArchivePortfolioHandler>();
builder.Services.AddScoped<RestorePortfolioHandler>();
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
builder.Services.AddScoped<AddSavingsAccountHandler>();
builder.Services.AddScoped<UpdateSavingsAccountHandler>();
builder.Services.AddScoped<GetSavingsAccountHandler>();
builder.Services.AddScoped<ListSavingsAccountsHandler>();
builder.Services.AddScoped<GetSavingsInterestPreviewHandler>();
builder.Services.AddScoped<SettleSavingsInterestHandler>();
builder.Services.AddScoped<UndoSavingsInterestSettlementHandler>();
builder.Services.AddScoped<ListTransferCandidatesHandler>();
builder.Services.AddScoped<CreateTransferHandler>();
builder.Services.AddScoped<DeleteTransferHandler>();

var app = builder.Build();

app.UseServiceDefaults();
app.MapDefaultEndpoints();
app.MapServiceOpenApi("portfolio");

app.MapCreatePortfolioEndpoint();
app.MapUpdatePortfolioEndpoint();
app.MapListPortfoliosEndpoint();
app.MapGetPortfolioEndpoint();
app.MapDeletePortfolioEndpoint();
app.MapArchivePortfolioEndpoint();
app.MapRestorePortfolioEndpoint();
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
app.MapAddSavingsAccountEndpoint();
app.MapUpdateSavingsAccountEndpoint();
app.MapGetSavingsAccountEndpoint();
app.MapListSavingsAccountsEndpoint();
app.MapGetSavingsInterestPreviewEndpoint();
app.MapSettleSavingsInterestEndpoint();
app.MapUndoSavingsInterestSettlementEndpoint();
app.MapListTransferCandidatesEndpoint();
app.MapCreateTransferEndpoint();
app.MapDeleteTransferEndpoint();

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
