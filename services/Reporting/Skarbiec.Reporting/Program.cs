using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Features.GetDashboard;
using Skarbiec.Reporting.Features.GetNetWorthHistory;
using Skarbiec.Reporting.MarketData;
using Skarbiec.Reporting.Messaging;
using Skarbiec.Reporting.Valuation;
using Skarbiec.ServiceDefaults.Messaging;
using Skarbiec.ServiceDefaults.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddServiceOpenApi();

// Registered so a test can swap in a fake clock.
builder.Services.TryAddSingleton(TimeProvider.System);

builder.Services.AddScoped<GetDashboardHandler>();
builder.Services.AddScoped<GetNetWorthHistoryHandler>();

if (!OpenApiBuildTime.IsActive)
{
    // Not AddNpgsqlDbContext: it always pools, and a pooled context cannot take the request-scoped ICurrentUser.
    var reportingConnectionString = builder.Configuration.GetConnectionString("reporting-db")
        ?? throw new InvalidOperationException("Missing connection string 'reporting-db'.");
    builder.Services.AddDbContext<ReportingDbContext>(options => options.UseNpgsql(reportingConnectionString));

    // Scoped, so it gets the consume scope's DbContext.
    builder.Services.AddScoped<PortfolioSnapshotWriter>();

    // Arity-1 AddConsumer<T> with the definition as a Type: the two-generic form does not compile.
    builder.AddRabbitMqMessaging<WebApplicationBuilder, ReportingDbContext>(
        configureConsumers: x =>
        {
            x.AddConsumer<DailyPricesSyncedConsumer>(typeof(DailyPricesSyncedConsumerDefinition));
            x.AddConsumer<AssetPositionChangedConsumer>(typeof(AssetPositionChangedConsumerDefinition));
            x.AddConsumer<AssetRemovedConsumer>(typeof(AssetRemovedConsumerDefinition));
            x.AddConsumer<PortfolioArchivedConsumer>(typeof(PortfolioArchivedConsumerDefinition));
            x.AddConsumer<PortfolioRestoredConsumer>(typeof(PortfolioRestoredConsumerDefinition));
            x.AddConsumer<PortfolioDeletedConsumer>(typeof(PortfolioDeletedConsumerDefinition));
            x.AddConsumer<PortfolioHistoryRebuildConsumer>(typeof(PortfolioHistoryRebuildConsumerDefinition));
        });

    // The daily prices and FX batch and their history twins, sent to MarketData's /internal endpoints with no token.
    builder.Services.AddHttpClient<IPriceQuoteClient, MarketDataPriceClient>(client =>
    {
        client.BaseAddress = new Uri("https+http://marketdata-service");
    });
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
