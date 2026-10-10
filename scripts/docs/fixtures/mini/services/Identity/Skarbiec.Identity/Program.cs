var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapLoginEndpoint();
app.MapGet("/api/identity/me", () => "me");

app.Run();
