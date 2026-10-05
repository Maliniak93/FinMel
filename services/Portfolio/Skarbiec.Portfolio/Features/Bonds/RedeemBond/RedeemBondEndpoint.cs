using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Bonds.RedeemBond;

public static class RedeemBondEndpoint
{
    public static IEndpointRouteBuilder MapRedeemBondEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/portfolios/{portfolioId:guid}/bonds");

        group.MapPost("{assetId:guid}/redemption", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<BondResponse>, ProblemHttpResult>> HandleAsync(
        Guid portfolioId, Guid assetId, RedeemBondRequest request, RedeemBondHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(portfolioId, assetId, request, cancellationToken)).ToHttpResult();
}
