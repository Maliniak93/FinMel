using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Bonds.RedeemBondEarly;

public static class RedeemBondEarlyEndpoint
{
    public static IEndpointRouteBuilder MapRedeemBondEarlyEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/bonds");

        group.MapPost("{assetId:guid}/early-redemption", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<BondResponse>, ProblemHttpResult>> HandleAsync(
        Guid assetId, RedeemBondEarlyRequest request, RedeemBondEarlyHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(assetId, request, cancellationToken)).ToHttpResult();
}
