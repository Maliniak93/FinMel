using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Accounts.ArchiveAccount;

public static class ArchiveAccountEndpoint
{
    public static IEndpointRouteBuilder MapArchiveAccountEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/accounts");

        group.MapPost("{id:guid}/archive", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<AccountResponse>, ProblemHttpResult>> HandleAsync(
        Guid id, ArchiveAccountHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, cancellationToken)).ToHttpResult();
}
