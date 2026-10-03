using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Identity.Data;
using Skarbiec.Identity.Features.Login;
using Skarbiec.Identity.Features.Logout;
using Skarbiec.Identity.Features.Refresh;
using Skarbiec.Identity.Features.Register;
using Skarbiec.Identity.Security;
using Skarbiec.ServiceDefaults.Authentication;
using Skarbiec.ServiceDefaults.Messaging;
using Skarbiec.ServiceDefaults.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddServiceOpenApi();

if (!OpenApiBuildTime.IsActive)
{
    builder.AddNpgsqlDbContext<IdentityDbContext>("identity-db");

    builder.AddRabbitMqMessaging<WebApplicationBuilder, IdentityDbContext>();

    builder.Services
        .AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = true)
        .AddEntityFrameworkStores<IdentityDbContext>()
        .AddSignInManager();
}

builder.Services.AddValidation();

builder.Services.AddScoped<RegisterHandler>();
builder.Services.AddScoped<LoginHandler>();
builder.Services.AddScoped<RefreshHandler>();
builder.Services.AddScoped<LogoutHandler>();
builder.Services.AddSingleton<AccessTokenGenerator>();

var app = builder.Build();

app.UseServiceDefaults();
app.MapDefaultEndpoints();
app.MapServiceOpenApi("identity");

app.MapRegisterEndpoint();
app.MapLoginEndpoint();
app.MapRefreshEndpoint();
app.MapLogoutEndpoint();

// Diagnostic endpoint proving a token minted by /login passes the shared JWT bearer validation.
app.MapGet("/api/identity/me", (ICurrentUser currentUser) => TypedResults.Ok(currentUser.UserId))
    .RequireAuthorization();

// Production applies migrations as an explicit deploy step instead (see deploy/README.md).
if (!OpenApiBuildTime.IsActive && app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
}

app.Run();

// Exposed for Skarbiec.Identity.Tests' WebApplicationFactory<Program>.
public partial class Program;
