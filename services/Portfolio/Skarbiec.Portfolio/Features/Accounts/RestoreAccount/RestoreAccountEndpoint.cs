using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Accounts.RestoreAccount;

public static class RestoreAccountEndpoint
{
    public static IEndpointRouteBuilder MapRestoreAccountEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/accounts");

        group.MapPost("{id:guid}/restore", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<AccountResponse>, ProblemHttpResult>> HandleAsync(
        Guid id, RestoreAccountHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, cancellationToken)).ToHttpResult();
}
