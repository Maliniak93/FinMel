using Microsoft.AspNetCore.Http.HttpResults;

namespace Skarbiec.Portfolio.Features.SavingsAccounts.ListSavingsAccounts;

public static class ListSavingsAccountsEndpoint
{
    public static IEndpointRouteBuilder MapListSavingsAccountsEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/savings-accounts");

        group.MapGet("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Ok<IReadOnlyList<SavingsAccountResponse>>> HandleAsync(
        ListSavingsAccountsHandler handler, CancellationToken cancellationToken)
        => TypedResults.Ok(await handler.HandleAsync(cancellationToken));
}
