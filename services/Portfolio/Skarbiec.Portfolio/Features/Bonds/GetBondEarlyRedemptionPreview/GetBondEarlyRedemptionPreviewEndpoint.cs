using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Bonds.GetBondEarlyRedemptionPreview;

public static class GetBondEarlyRedemptionPreviewEndpoint
{
    public static IEndpointRouteBuilder MapGetBondEarlyRedemptionPreviewEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/bonds");

        group.MapGet("{assetId:guid}/early-redemption-preview", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<BondEarlyRedemptionPreviewResponse>, ProblemHttpResult>> HandleAsync(
        Guid assetId,
        DateOnly date,
        int bondCount,
        decimal? runningPeriodRatePercent,
        GetBondEarlyRedemptionPreviewHandler handler,
        CancellationToken cancellationToken)
        => (await handler.HandleAsync(assetId, date, bondCount, runningPeriodRatePercent, cancellationToken))
            .ToHttpResult();
}
