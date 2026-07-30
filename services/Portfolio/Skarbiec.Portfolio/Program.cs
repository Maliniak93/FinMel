using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Plain scoped AddDbContext, not Aspire's AddNpgsqlDbContext — that helper always pools
// (AddDbContextPool), which can't take the constructor-injected, request-scoped ICurrentUser
// PortfolioDbContext needs for tenancy (ADR-006).
var portfolioConnectionString = builder.Configuration.GetConnectionString("portfolio-db")
    ?? throw new InvalidOperationException("Missing connection string 'portfolio-db'.");
builder.Services.AddDbContext<PortfolioDbContext>(options => options.UseNpgsql(portfolioConnectionString));

var app = builder.Build();

app.UseServiceDefaults();
app.MapDefaultEndpoints();

// Production applies migrations as an explicit deploy step instead (see deploy/README.md).
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<PortfolioDbContext>().Database.MigrateAsync();
}

app.Run();

// Exposed for Skarbiec.Portfolio.Tests' WebApplicationFactory<Program>.
public partial class Program;
