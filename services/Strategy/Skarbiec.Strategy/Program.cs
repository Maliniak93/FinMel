using Microsoft.EntityFrameworkCore;
using Skarbiec.Strategy.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Plain scoped AddDbContext, not Aspire's AddNpgsqlDbContext — that helper always pools
// (AddDbContextPool), which can't take the constructor-injected, request-scoped ICurrentUser
// StrategyDbContext needs for tenancy (ADR-006).
var strategyConnectionString = builder.Configuration.GetConnectionString("strategy-db")
    ?? throw new InvalidOperationException("Missing connection string 'strategy-db'.");
builder.Services.AddDbContext<StrategyDbContext>(options => options.UseNpgsql(strategyConnectionString));

var app = builder.Build();

app.UseServiceDefaults();
app.MapDefaultEndpoints();

// Production applies migrations as an explicit deploy step instead (see deploy/README.md).
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<StrategyDbContext>().Database.MigrateAsync();
}

app.Run();

// Exposed for Skarbiec.Strategy.Tests' WebApplicationFactory<Program>.
public partial class Program;
