using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Features.GetDashboard;
using Skarbiec.Reporting.Features.GetNetWorthHistory;
using Skarbiec.Reporting.MarketData;
using Skarbiec.Reporting.Messaging;
using Skarbiec.Reporting.Portfolio;
using Skarbiec.ServiceDefaults.Http;
using Skarbiec.ServiceDefaults.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddServiceOpenApi();

// Plain scoped AddDbContext, not Aspire's AddNpgsqlDbContext — that helper always pools
// (AddDbContextPool), which can't take the constructor-injected, request-scoped ICurrentUser
// ReportingDbContext needs for tenancy (ADR-006).
var reportingConnectionString = builder.Configuration.GetConnectionString("reporting-db")
    ?? throw new InvalidOperationException("Missing connection string 'reporting-db'.");
builder.Services.AddDbContext<ReportingDbContext>(options => options.UseNpgsql(reportingConnectionString));

// T2.12: GetNetWorthHistory resolves the 1M/1Y/YTD range boundaries against "today" — same
// TimeProvider.System registration MarketData's Quartz jobs use, so tests can inject a fake later
// without touching production wiring.
builder.Services.TryAddSingleton(TimeProvider.System);

builder.Services.AddScoped<GetDashboardHandler>();
builder.Services.AddScoped<GetNetWorthHistoryHandler>();

// T2.11: consumes DailyPricesSynced (published by MarketData, T2.10) through the T0.12 idempotent
// inbox template.
builder.AddRabbitMqMessaging<WebApplicationBuilder, ReportingDbContext>(
    configureConsumers: x => x.AddConsumer<DailyPricesSyncedConsumer>(typeof(DailyPricesSyncedConsumerDefinition)));

// The consumer has no caller JWT to forward (it's triggered by a message, not a request) and needs
// every user's data, not one — SystemTokenHandler mints a SystemCaller token instead of
// JwtForwardingHandler's token passthrough.
builder.Services.AddHttpClient<IPositionsClient, PortfolioPositionsClient>(client =>
{
    client.BaseAddress = new Uri("https+http://portfolio-service");
}).AddSystemTokenHandler();

builder.Services.AddHttpClient<IPriceQuoteClient, MarketDataPriceClient>(client =>
{
    client.BaseAddress = new Uri("https+http://marketdata-service");
}).AddSystemTokenHandler();

var app = builder.Build();

app.UseServiceDefaults();
app.MapDefaultEndpoints();
app.MapServiceOpenApi("reporting");

app.MapGetDashboardEndpoint();
app.MapGetNetWorthHistoryEndpoint();

// Production applies migrations as an explicit deploy step instead (see deploy/README.md).
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<ReportingDbContext>().Database.MigrateAsync();
}

app.Run();

// Exposed for Skarbiec.Reporting.Tests' WebApplicationFactory<Program>.
public partial class Program;
