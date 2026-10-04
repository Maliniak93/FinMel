using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Bonds.UndoBondInterestSettlement;

public static class UndoBondInterestSettlementEndpoint
{
    public static IEndpointRouteBuilder MapUndoBondInterestSettlementEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/portfolios/{portfolioId:guid}/bonds");

        group.MapDelete("{assetId:guid}/interest-settlements/{settlementId:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid portfolioId,
        Guid assetId,
        Guid settlementId,
        UndoBondInterestSettlementHandler handler,
        CancellationToken cancellationToken)
        => (await handler.HandleAsync(portfolioId, assetId, settlementId, cancellationToken)).ToHttpResult();
}
