using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Accounts.UpdateAccount;

public static class UpdateAccountEndpoint
{
    public static IEndpointRouteBuilder MapUpdateAccountEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/accounts");

        group.MapPut("{id:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<AccountResponse>, ProblemHttpResult>> HandleAsync(
        Guid id, UpdateAccountRequest request, UpdateAccountHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, request, cancellationToken)).ToHttpResult();
}
