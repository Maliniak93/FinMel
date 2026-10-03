using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Features.AddCustomInstrument;
using Skarbiec.MarketData.Features.GetBondSeries;
using Skarbiec.MarketData.Features.GetFxRate;
using Skarbiec.MarketData.Features.GetFxRatesBatch;
using Skarbiec.MarketData.Features.GetInstrument;
using Skarbiec.MarketData.Features.GetLatestPricesBatch;
using Skarbiec.MarketData.Features.GetSyncStatus;
using Skarbiec.MarketData.Features.ListBondSeries;
using Skarbiec.MarketData.Features.SearchInstruments;
using Skarbiec.MarketData.Features.TriggerSync;
using Skarbiec.MarketData.Messaging;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Sources.CoinGecko;
using Skarbiec.MarketData.Sources.MfBonds;
using Skarbiec.MarketData.Sources.Nbp;
using Skarbiec.MarketData.Sources.Stooq;
using Skarbiec.MarketData.Sources.Verification;
using Skarbiec.ServiceDefaults.Messaging;
using Skarbiec.ServiceDefaults.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddServiceOpenApi();

builder.AddTickerVerification();

if (!OpenApiBuildTime.IsActive)
{
    builder.AddNpgsqlDbContext<MarketDataDbContext>("marketdata-db");

    builder.AddRabbitMqMessaging<WebApplicationBuilder, MarketDataDbContext>(
        configureConsumers: x =>
        {
            x.AddConsumer<AssetPositionChangedConsumer>(typeof(AssetPositionChangedConsumerDefinition));
            x.AddConsumer<AssetRemovedConsumer>(typeof(AssetRemovedConsumerDefinition));
        });

    builder.AddNbpSources();
    builder.AddStooqSource();
    builder.AddCoinGeckoSource();
    builder.AddMfBondSource();
    builder.AddPriceSyncJob();
    // After AddPriceSyncJob: it reuses that call's scheduler.
    builder.AddFxSyncJob();
    builder.AddHistoryBackfillJob();
    builder.AddBondCatalogSyncJob();
    builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing
        .AddSource(PriceSyncJob.ActivitySourceName)
        .AddSource(FxSyncJob.ActivitySourceName)
        .AddSource(HistoryBackfillJob.ActivitySourceName)
        .AddSource(BondCatalogSyncJob.ActivitySourceName));
}

builder.Services.AddValidation();
builder.Services.TryAddSingleton(TimeProvider.System);

builder.Services.AddScoped<SearchInstrumentsHandler>();
builder.Services.AddScoped<AddCustomInstrumentHandler>();
builder.Services.AddScoped<GetInstrumentHandler>();
builder.Services.AddScoped<GetLatestPricesBatchHandler>();
builder.Services.AddScoped<GetFxRatesBatchHandler>();
builder.Services.AddScoped<GetFxRateHandler>();
builder.Services.AddScoped<TriggerSyncHandler>();
builder.Services.AddScoped<GetSyncStatusHandler>();
builder.Services.AddScoped<ListBondSeriesHandler>();
builder.Services.AddScoped<GetBondSeriesHandler>();

var app = builder.Build();

app.UseServiceDefaults();
app.MapDefaultEndpoints();
app.MapServiceOpenApi("marketdata");

app.MapSearchInstrumentsEndpoint();
app.MapAddCustomInstrumentEndpoint();
app.MapGetInstrumentEndpoint();
app.MapGetLatestPricesBatchEndpoint();
app.MapGetFxRatesBatchEndpoint();
app.MapGetFxRateEndpoint();
app.MapTriggerSyncEndpoint();
app.MapGetSyncStatusEndpoint();
app.MapListBondSeriesEndpoint();
app.MapGetBondSeriesEndpoint();

// Production applies migrations and the seed as an explicit deploy step instead (see deploy/README.md).
if (!OpenApiBuildTime.IsActive && app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
    await db.Database.MigrateAsync();
    await MarketDataSeeder.SeedAsync(db);
}

app.Run();

// Exposed for Skarbiec.MarketData.Tests' WebApplicationFactory<Program>.
public partial class Program;
