using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.Portfolio.Data;

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
        int? year, AccountType? type, ListAccountsHandler handler, CancellationToken cancellationToken)
        => TypedResults.Ok(await handler.HandleAsync(year, type, cancellationToken));
}
