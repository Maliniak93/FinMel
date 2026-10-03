using Microsoft.AspNetCore.Http.HttpResults;

namespace Skarbiec.Portfolio.Features.Bonds.ListBonds;

public static class ListBondsEndpoint
{
    public static IEndpointRouteBuilder MapListBondsEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/bonds");

        group.MapGet("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Ok<IReadOnlyList<BondResponse>>> HandleAsync(
        ListBondsHandler handler, CancellationToken cancellationToken)
        => TypedResults.Ok(await handler.HandleAsync(cancellationToken));
}
