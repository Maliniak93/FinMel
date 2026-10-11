using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Bonds.UpdateBond;

public static class UpdateBondEndpoint
{
    public static IEndpointRouteBuilder MapUpdateBondEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/bonds");

        group.MapPut("{assetId:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<BondResponse>, ProblemHttpResult>> HandleAsync(
        Guid assetId, UpdateBondRequest request, UpdateBondHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(assetId, request, cancellationToken)).ToHttpResult();
}
