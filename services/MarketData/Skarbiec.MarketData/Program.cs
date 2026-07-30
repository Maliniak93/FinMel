using Microsoft.EntityFrameworkCore;
using Skarbiec.MarketData.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddNpgsqlDbContext<MarketDataDbContext>("marketdata-db");

var app = builder.Build();

app.UseServiceDefaults();
app.MapDefaultEndpoints();

// Production applies migrations as an explicit deploy step instead (see deploy/README.md).
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<MarketDataDbContext>().Database.MigrateAsync();
}

app.Run();

// Exposed for Skarbiec.MarketData.Tests' WebApplicationFactory<Program>.
public partial class Program;
