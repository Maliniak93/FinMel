using Microsoft.AspNetCore.Http.HttpResults;

namespace Skarbiec.Portfolio.Features.Deposits.ListDeposits;

public static class ListDepositsEndpoint
{
    public static IEndpointRouteBuilder MapListDepositsEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/deposits");

        group.MapGet("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Ok<IReadOnlyList<DepositResponse>>> HandleAsync(
        ListDepositsHandler handler, CancellationToken cancellationToken)
        => TypedResults.Ok(await handler.HandleAsync(cancellationToken));
}
