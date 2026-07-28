using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Identity.Features.Logout;

public static class LogoutEndpoint
{
    private const string RefreshTokenCookieName = "refreshToken";

    public static IEndpointRouteBuilder MapLogoutEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/identity/logout");

        group.MapPost("", HandleAsync);

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        LogoutHandler handler, HttpContext httpContext, CancellationToken cancellationToken)
    {
        httpContext.Request.Cookies.TryGetValue(RefreshTokenCookieName, out var rawRefreshToken);

        var result = await handler.HandleAsync(rawRefreshToken, cancellationToken);

        httpContext.Response.Cookies.Delete(RefreshTokenCookieName, new CookieOptions { Path = "/api/identity" });

        return result.ToHttpResult();
    }
}
