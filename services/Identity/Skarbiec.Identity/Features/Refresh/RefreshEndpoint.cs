using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Identity.Features.Refresh;

public static class RefreshEndpoint
{
    private const string RefreshTokenCookieName = "refreshToken";

    public static IEndpointRouteBuilder MapRefreshEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/identity/refresh");

        group.MapPost("", HandleAsync);

        return app;
    }

    private static async Task<Results<Ok<RefreshResponse>, ProblemHttpResult>> HandleAsync(
        RefreshHandler handler, HttpContext httpContext, CancellationToken cancellationToken)
    {
        httpContext.Request.Cookies.TryGetValue(RefreshTokenCookieName, out var rawRefreshToken);

        var result = await handler.HandleAsync(rawRefreshToken, cancellationToken);

        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        httpContext.Response.Cookies.Append(RefreshTokenCookieName, result.Value.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = result.Value.RefreshTokenExpiresAtUtc,
            Path = "/api/identity"
        });

        return TypedResults.Ok(result.Value.Response);
    }
}
