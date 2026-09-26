using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Deposits.GetSettlementPreview;

public static class GetSettlementPreviewEndpoint
{
    public static IEndpointRouteBuilder MapGetSettlementPreviewEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/portfolios/{portfolioId:guid}/deposits");

        group.MapGet("{assetId:guid}/settlement-preview", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<DepositSettlementPreviewResponse>, ProblemHttpResult>> HandleAsync(
        Guid portfolioId, Guid assetId, GetSettlementPreviewHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(portfolioId, assetId, cancellationToken)).ToHttpResult();
}
