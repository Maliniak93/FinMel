using Microsoft.EntityFrameworkCore;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Features.AddCustomInstrument;
using Skarbiec.MarketData.Features.GetInstrument;
using Skarbiec.MarketData.Features.SearchInstruments;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Sources.CoinGecko;
using Skarbiec.MarketData.Sources.Nbp;
using Skarbiec.MarketData.Sources.Stooq;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddServiceOpenApi();

builder.AddNpgsqlDbContext<MarketDataDbContext>("marketdata-db");
builder.AddNbpSources();
builder.AddStooqSource();
builder.AddCoinGeckoSource();
builder.AddPriceSyncJob();
builder.AddHistoryBackfillJob();
builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing
    .AddSource(PriceSyncJob.ActivitySourceName)
    .AddSource(HistoryBackfillJob.ActivitySourceName));

builder.Services.AddValidation();

builder.Services.AddScoped<SearchInstrumentsHandler>();
builder.Services.AddScoped<AddCustomInstrumentHandler>();
builder.Services.AddScoped<GetInstrumentHandler>();

var app = builder.Build();

app.UseServiceDefaults();
app.MapDefaultEndpoints();
app.MapServiceOpenApi("marketdata");

app.MapSearchInstrumentsEndpoint();
app.MapAddCustomInstrumentEndpoint();
app.MapGetInstrumentEndpoint();

// Production applies migrations (and the seed below) as an explicit deploy step instead (see
// deploy/README.md).
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
    await db.Database.MigrateAsync();
    await MarketDataSeeder.SeedAsync(db);
}

app.Run();

// Exposed for Skarbiec.MarketData.Tests' WebApplicationFactory<Program>.
public partial class Program;
