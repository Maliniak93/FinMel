using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Bonds.SwapBond;

public static class SwapBondEndpoint
{
    public static IEndpointRouteBuilder MapSwapBondEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/bonds");

        group.MapPost("{assetId:guid}/swap", HandleAsync).RequireAuthorization();

        return app;
    }

    // The 201 carries the new bond; the old one is read back through GetBond.
    private static async Task<Results<Created<BondResponse>, ProblemHttpResult>> HandleAsync(
        Guid assetId, SwapBondRequest request, SwapBondHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(assetId, request, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/portfolio/bonds/{result.Value.AssetId}", result.Value)
            : result.Error.ToProblem();
    }
}
