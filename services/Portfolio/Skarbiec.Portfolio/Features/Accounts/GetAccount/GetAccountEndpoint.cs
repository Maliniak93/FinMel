using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Accounts.GetAccount;

public static class GetAccountEndpoint
{
    public static IEndpointRouteBuilder MapGetAccountEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/accounts");

        group.MapGet("{id:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<AccountResponse>, ProblemHttpResult>> HandleAsync(
        Guid id, int? year, GetAccountHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, year, cancellationToken)).ToHttpResult();
}
