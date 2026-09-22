using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Features.GetDashboard;
using Skarbiec.Reporting.Features.GetNetWorthHistory;
using Skarbiec.Reporting.MarketData;
using Skarbiec.Reporting.Messaging;
using Skarbiec.ServiceDefaults.Http;
using Skarbiec.ServiceDefaults.Messaging;
using Skarbiec.ServiceDefaults.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddServiceOpenApi();

// T2.12: GetNetWorthHistory resolves the 1M/1Y/YTD range boundaries against "today" — same
// TimeProvider.System registration MarketData's Quartz jobs use, so tests can inject a fake later
// without touching production wiring.
builder.Services.TryAddSingleton(TimeProvider.System);

builder.Services.AddScoped<GetDashboardHandler>();
builder.Services.AddScoped<GetNetWorthHistoryHandler>();

if (!OpenApiBuildTime.IsActive)
{
    // Plain scoped AddDbContext, not Aspire's AddNpgsqlDbContext — that helper always pools
    // (AddDbContextPool), which can't take the constructor-injected, request-scoped ICurrentUser
    // ReportingDbContext needs for tenancy (ADR-006).
    var reportingConnectionString = builder.Configuration.GetConnectionString("reporting-db")
        ?? throw new InvalidOperationException("Missing connection string 'reporting-db'.");
    builder.Services.AddDbContext<ReportingDbContext>(options => options.UseNpgsql(reportingConnectionString));

    // Consumes DailyPricesSynced (published by MarketData, T2.10) plus Portfolio's position and
    // portfolio-lifecycle events (spec-02/spec-03), each through the T0.12 idempotent inbox
    // template. Arity-1 AddConsumer<T> with the definition as a Type — the two-generic form doesn't
    // compile here (.claude/rules/messaging.md).
    builder.AddRabbitMqMessaging<WebApplicationBuilder, ReportingDbContext>(
        configureConsumers: x =>
        {
            x.AddConsumer<DailyPricesSyncedConsumer>(typeof(DailyPricesSyncedConsumerDefinition));
            x.AddConsumer<AssetPositionChangedConsumer>(typeof(AssetPositionChangedConsumerDefinition));
            x.AddConsumer<AssetRemovedConsumer>(typeof(AssetRemovedConsumerDefinition));
            x.AddConsumer<PortfolioArchivedConsumer>(typeof(PortfolioArchivedConsumerDefinition));
            x.AddConsumer<PortfolioRestoredConsumer>(typeof(PortfolioRestoredConsumerDefinition));
            x.AddConsumer<PortfolioDeletedConsumer>(typeof(PortfolioDeletedConsumerDefinition));
        });

    // The one surviving cross-service REST call (ADR-021): the daily prices/FX batch. The consumer
    // has no caller JWT to forward (it's triggered by a message, not a request) and needs every
    // user's data, not one — SystemTokenHandler mints a SystemCaller token instead of
    // JwtForwardingHandler's token passthrough.
    builder.Services.AddHttpClient<IPriceQuoteClient, MarketDataPriceClient>(client =>
    {
        client.BaseAddress = new Uri("https+http://marketdata-service");
    }).AddSystemTokenHandler();
}

var app = builder.Build();

app.UseServiceDefaults();
app.MapDefaultEndpoints();
app.MapServiceOpenApi("reporting");

app.MapGetDashboardEndpoint();
app.MapGetNetWorthHistoryEndpoint();

// Production applies migrations as an explicit deploy step instead (see deploy/README.md).
if (!OpenApiBuildTime.IsActive && app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<ReportingDbContext>().Database.MigrateAsync();
}

app.Run();

// Exposed for Skarbiec.Reporting.Tests' WebApplicationFactory<Program>.
public partial class Program;
