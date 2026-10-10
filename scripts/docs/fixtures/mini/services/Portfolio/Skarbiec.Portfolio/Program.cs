var builder = WebApplication.CreateBuilder(args);

builder.AddRabbitMqMessaging<WebApplicationBuilder, PortfolioDbContext>();

var app = builder.Build();

app.MapAddAssetEndpoint();
app.MapGetBondEndpoint();
app.MapGet("/api/portfolio/ping", () => "pong");

app.Run();
