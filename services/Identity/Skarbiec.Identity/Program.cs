using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Identity.Data;
using Skarbiec.Identity.Features.Login;
using Skarbiec.Identity.Features.Logout;
using Skarbiec.Identity.Features.Refresh;
using Skarbiec.Identity.Features.Register;
using Skarbiec.Identity.Security;
using Skarbiec.ServiceDefaults.Authentication;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddNpgsqlDbContext<IdentityDbContext>("identity-db");

builder.Services.AddValidation();

builder.Services
    .AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = true)
    .AddEntityFrameworkStores<IdentityDbContext>()
    .AddSignInManager();

builder.Services.AddScoped<RegisterHandler>();
builder.Services.AddScoped<LoginHandler>();
builder.Services.AddScoped<RefreshHandler>();
builder.Services.AddScoped<LogoutHandler>();
builder.Services.AddSingleton<AccessTokenGenerator>();

var app = builder.Build();

app.UseServiceDefaults();
app.MapDefaultEndpoints();

app.MapRegisterEndpoint();
app.MapLoginEndpoint();
app.MapRefreshEndpoint();
app.MapLogoutEndpoint();

// Diagnostic endpoint proving an access token minted by /login round-trips through JWT
// bearer validation (ServiceDefaults) end to end — mirrors Skarbiec.ServiceDefaults.Sample's /secure.
app.MapGet("/api/identity/me", (ICurrentUser currentUser) => TypedResults.Ok(currentUser.UserId))
    .RequireAuthorization();

// Production applies migrations as an explicit deploy step instead (see deploy/README.md).
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
}

app.Run();

// Exposed for Skarbiec.Identity.Tests' WebApplicationFactory<Program>.
public partial class Program;
