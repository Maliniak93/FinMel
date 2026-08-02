using Microsoft.EntityFrameworkCore;
using Skarbiec.Reporting.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddServiceOpenApi();

// Plain scoped AddDbContext, not Aspire's AddNpgsqlDbContext — that helper always pools
// (AddDbContextPool), which can't take the constructor-injected, request-scoped ICurrentUser
// ReportingDbContext needs for tenancy (ADR-006).
var reportingConnectionString = builder.Configuration.GetConnectionString("reporting-db")
    ?? throw new InvalidOperationException("Missing connection string 'reporting-db'.");
builder.Services.AddDbContext<ReportingDbContext>(options => options.UseNpgsql(reportingConnectionString));

var app = builder.Build();

app.UseServiceDefaults();
app.MapDefaultEndpoints();
app.MapServiceOpenApi("reporting");

// Production applies migrations as an explicit deploy step instead (see deploy/README.md).
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<ReportingDbContext>().Database.MigrateAsync();
}

app.Run();

// Exposed for Skarbiec.Reporting.Tests' WebApplicationFactory<Program>.
public partial class Program;
