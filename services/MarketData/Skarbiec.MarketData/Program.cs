using Microsoft.EntityFrameworkCore;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources.Nbp;
using Skarbiec.MarketData.Sources.Stooq;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddServiceOpenApi();

builder.AddNpgsqlDbContext<MarketDataDbContext>("marketdata-db");
builder.AddNbpSources();
builder.AddStooqSource();

var app = builder.Build();

app.UseServiceDefaults();
app.MapDefaultEndpoints();
app.MapServiceOpenApi("marketdata");

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
