namespace Skarbiec.Identity.Features.Login;

public static class LoginEndpoint
{
    public static IEndpointRouteBuilder MapLoginEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/identity/login");

        group.MapPost("/token", HandleAsync);

        return app;
    }
}
