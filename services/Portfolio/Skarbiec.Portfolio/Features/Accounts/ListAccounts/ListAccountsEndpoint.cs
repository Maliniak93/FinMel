using Microsoft.AspNetCore.Http.HttpResults;

namespace Skarbiec.Portfolio.Features.Accounts.ListAccounts;

public static class ListAccountsEndpoint
{
    public static IEndpointRouteBuilder MapListAccountsEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/accounts");

        group.MapGet("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Ok<IReadOnlyList<AccountResponse>>> HandleAsync(
        int? year, ListAccountsHandler handler, CancellationToken cancellationToken)
        => TypedResults.Ok(await handler.HandleAsync(year, cancellationToken));
}
