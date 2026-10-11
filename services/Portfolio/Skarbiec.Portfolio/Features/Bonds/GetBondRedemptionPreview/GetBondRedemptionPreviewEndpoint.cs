using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Bonds.GetBondRedemptionPreview;

public static class GetBondRedemptionPreviewEndpoint
{
    public static IEndpointRouteBuilder MapGetBondRedemptionPreviewEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/bonds");

        group.MapGet("{assetId:guid}/redemption-preview", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<BondRedemptionPreviewResponse>, ProblemHttpResult>> HandleAsync(
        Guid assetId, GetBondRedemptionPreviewHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(assetId, cancellationToken)).ToHttpResult();
}
