using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.ArchivePortfolio;
using Skarbiec.Portfolio.Features.CreatePortfolio;
using Skarbiec.Portfolio.Features.DeletePortfolio;
using Skarbiec.Portfolio.Features.DeleteTransaction;
using Skarbiec.Portfolio.Features.GetAsset;
using Skarbiec.Portfolio.Features.GetPortfolio;
using Skarbiec.Portfolio.Features.ListAssets;
using Skarbiec.Portfolio.Features.ListPortfolios;
using Skarbiec.Portfolio.Features.ListTransactions;
using Skarbiec.Portfolio.Features.RecordTransaction;
using Skarbiec.Portfolio.Features.RemoveAsset;
using Skarbiec.Portfolio.Features.RestorePortfolio;
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
    // Plain scoped AddDbContext, not Aspire's AddNpgsqlDbContext — that helper always pools
    // (AddDbContextPool), which can't take the constructor-injected, request-scoped ICurrentUser
    // PortfolioDbContext needs for tenancy (ADR-006).
    var portfolioConnectionString = builder.Configuration.GetConnectionString("portfolio-db")
        ?? throw new InvalidOperationException("Missing connection string 'portfolio-db'.");
    builder.Services.AddDbContext<PortfolioDbContext>(options => options.UseNpgsql(portfolioConnectionString));

    // No consumers — Portfolio only publishes (AssetPositionChanged/AssetRemoved and the
    // Portfolio* lifecycle events, spec-02) through the outbox; Reporting consumes them (spec-03)
    // and MarketData joins in spec-04.
    builder.AddRabbitMqMessaging<WebApplicationBuilder, PortfolioDbContext>();

    // AddAsset/UpdateAsset validate a market asset's InstrumentId against MarketData (T2.9) — resilience
    // and service discovery come from ServiceDefaults' ConfigureHttpClientDefaults; the call goes to
    // MarketData's /internal endpoint with no token (ADR-027).
    builder.Services.AddHttpClient<IInstrumentLookupClient, MarketDataInstrumentLookupClient>(client =>
    {
        client.BaseAddress = new Uri("https+http://marketdata-service");
    });

    // Transaction writes freeze the transaction-date PLN rate (ADR-026) — same wiring as above:
    // MarketData's /internal endpoint, no token.
    builder.Services.AddHttpClient<IFxRateLookupClient, MarketDataFxRateLookupClient>(client =>
    {
        client.BaseAddress = new Uri("https+http://marketdata-service");
    });
}

builder.Services.AddValidation();

// Same TimeProvider.System registration Reporting and MarketData's jobs use, so a test can inject a
// fake clock and pin an event's OccurredAtUtc (spec-02 design decision 5).
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
builder.Services.AddScoped<RecordTransactionHandler>();
builder.Services.AddScoped<ListTransactionsHandler>();
builder.Services.AddScoped<UpdateTransactionHandler>();
builder.Services.AddScoped<DeleteTransactionHandler>();

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
app.MapRecordTransactionEndpoint();
app.MapListTransactionsEndpoint();
app.MapUpdateTransactionEndpoint();
app.MapDeleteTransactionEndpoint();

// Diagnostic endpoint proving a Gateway-forwarded JWT authorizes a call routed to a skeleton
// service (T0.15 AC) — mirrors Skarbiec.Identity's /api/identity/me.
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
