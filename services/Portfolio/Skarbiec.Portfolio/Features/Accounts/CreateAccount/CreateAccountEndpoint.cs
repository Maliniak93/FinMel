using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Accounts.CreateAccount;

public static class CreateAccountEndpoint
{
    public static IEndpointRouteBuilder MapCreateAccountEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/accounts");

        group.MapPost("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Created<AccountResponse>, ProblemHttpResult>> HandleAsync(
        CreateAccountRequest request, CreateAccountHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/portfolio/accounts/{result.Value.Id}", result.Value)
            : result.Error.ToProblem();
    }
}
