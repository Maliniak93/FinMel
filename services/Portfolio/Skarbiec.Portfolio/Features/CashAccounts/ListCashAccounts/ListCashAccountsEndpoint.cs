using Microsoft.AspNetCore.Http.HttpResults;

namespace Skarbiec.Portfolio.Features.CashAccounts.ListCashAccounts;

public static class ListCashAccountsEndpoint
{
    public static IEndpointRouteBuilder MapListCashAccountsEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/cash-accounts");

        group.MapGet("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Ok<CashAccountsResponse>> HandleAsync(
        ListCashAccountsHandler handler, CancellationToken cancellationToken)
        => TypedResults.Ok(await handler.HandleAsync(cancellationToken));
}
