using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.Portfolio.Features.Bonds.SettleBondInterest;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Bonds.PreviewBondInterest;

public static class PreviewBondInterestEndpoint
{
    public static IEndpointRouteBuilder MapPreviewBondInterestEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/bonds");

        group.MapPost("{assetId:guid}/interest-settlements/preview", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<BondInterestPreviewResponse>, ProblemHttpResult>> HandleAsync(
        Guid assetId,
        SettleBondInterestRequest request,
        PreviewBondInterestHandler handler,
        CancellationToken cancellationToken)
        => (await handler.HandleAsync(assetId, request, cancellationToken)).ToHttpResult();
}
