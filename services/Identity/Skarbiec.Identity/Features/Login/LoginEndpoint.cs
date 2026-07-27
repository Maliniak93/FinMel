using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Identity.Features.Login;

public static class LoginEndpoint
{
    private const string RefreshTokenCookieName = "refreshToken";

    public static IEndpointRouteBuilder MapLoginEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/identity/login");

        group.MapPost("", HandleAsync);

        return app;
    }

    private static async Task<Results<Ok<LoginResponse>, ProblemHttpResult>> HandleAsync(
        LoginRequest request, LoginHandler handler, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

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
