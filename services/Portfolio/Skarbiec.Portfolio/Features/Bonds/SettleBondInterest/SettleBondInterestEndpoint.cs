using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Bonds.SettleBondInterest;

public static class SettleBondInterestEndpoint
{
    public static IEndpointRouteBuilder MapSettleBondInterestEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/portfolios/{portfolioId:guid}/bonds");

        group.MapPost("{assetId:guid}/interest-settlements", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Created<IReadOnlyList<BondSettlementResponse>>, ProblemHttpResult>> HandleAsync(
        Guid portfolioId,
        Guid assetId,
        SettleBondInterestRequest request,
        SettleBondInterestHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(portfolioId, assetId, request, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/portfolio/portfolios/{portfolioId}/bonds/{assetId}", result.Value)
            : result.Error.ToProblem();
    }
}
