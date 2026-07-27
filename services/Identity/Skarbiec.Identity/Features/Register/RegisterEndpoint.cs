using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Identity.Features.Register;

public static class RegisterEndpoint
{
    public static IEndpointRouteBuilder MapRegisterEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/identity/register");

        group.MapPost("", HandleAsync);

        return app;
    }

    private static async Task<Results<Created<RegisterResponse>, ProblemHttpResult>> HandleAsync(
        RegisterRequest request, RegisterHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/identity/register/{result.Value.UserId}", result.Value)
            : result.Error.ToProblem();
    }
}
