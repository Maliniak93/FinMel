using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.ArchivePortfolio;
using Skarbiec.Portfolio.Features.CreatePortfolio;
using Skarbiec.Portfolio.Features.DeletePortfolio;
using Skarbiec.Portfolio.Features.GetPortfolio;
using Skarbiec.Portfolio.Features.ListPortfolios;
using Skarbiec.Portfolio.Features.UpdatePortfolio;
using Skarbiec.ServiceDefaults.Authentication;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Plain scoped AddDbContext, not Aspire's AddNpgsqlDbContext — that helper always pools
// (AddDbContextPool), which can't take the constructor-injected, request-scoped ICurrentUser
// PortfolioDbContext needs for tenancy (ADR-006).
var portfolioConnectionString = builder.Configuration.GetConnectionString("portfolio-db")
    ?? throw new InvalidOperationException("Missing connection string 'portfolio-db'.");
builder.Services.AddDbContext<PortfolioDbContext>(options => options.UseNpgsql(portfolioConnectionString));

builder.Services.AddValidation();

builder.Services.AddScoped<CreatePortfolioHandler>();
builder.Services.AddScoped<UpdatePortfolioHandler>();
builder.Services.AddScoped<ListPortfoliosHandler>();
builder.Services.AddScoped<GetPortfolioHandler>();
builder.Services.AddScoped<DeletePortfolioHandler>();
builder.Services.AddScoped<ArchivePortfolioHandler>();

var app = builder.Build();

app.UseServiceDefaults();
app.MapDefaultEndpoints();

app.MapCreatePortfolioEndpoint();
app.MapUpdatePortfolioEndpoint();
app.MapListPortfoliosEndpoint();
app.MapGetPortfolioEndpoint();
app.MapDeletePortfolioEndpoint();
app.MapArchivePortfolioEndpoint();

// Diagnostic endpoint proving a Gateway-forwarded JWT authorizes a call routed to a skeleton
// service (T0.15 AC) — mirrors Skarbiec.Identity's /api/identity/me.
app.MapGet("/api/portfolio/me", (ICurrentUser currentUser) => TypedResults.Ok(currentUser.UserId))
    .RequireAuthorization();

// Production applies migrations as an explicit deploy step instead (see deploy/README.md).
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<PortfolioDbContext>().Database.MigrateAsync();
}

app.Run();

// Exposed for Skarbiec.Portfolio.Tests' WebApplicationFactory<Program>.
public partial class Program;
