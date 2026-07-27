using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Identity.Data;
using Skarbiec.Identity.Features.Register;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddNpgsqlDbContext<IdentityDbContext>("identity-db");

builder.Services.AddValidation();

builder.Services
    .AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = true)
    .AddEntityFrameworkStores<IdentityDbContext>();

builder.Services.AddScoped<RegisterHandler>();

var app = builder.Build();

app.UseServiceDefaults();
app.MapDefaultEndpoints();

app.MapRegisterEndpoint();

// Production applies migrations as an explicit deploy step instead (see deploy/README.md).
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
}

app.Run();

// Exposed for Skarbiec.Identity.Tests' WebApplicationFactory<Program>.
public partial class Program;
