using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.SavingsAccounts.SettleSavingsInterest;

public static class SettleSavingsInterestEndpoint
{
    public static IEndpointRouteBuilder MapSettleSavingsInterestEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/portfolios/{portfolioId:guid}/savings-accounts");

        group.MapPost("{assetId:guid}/interest-settlements", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Created<SavingsInterestSettlementResponse>, ProblemHttpResult>> HandleAsync(
        Guid portfolioId,
        Guid assetId,
        SettleSavingsInterestRequest request,
        SettleSavingsInterestHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(portfolioId, assetId, request, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created(
                $"/api/portfolio/portfolios/{portfolioId}/savings-accounts/{assetId}/interest-settlements/{result.Value.SettlementId}",
                result.Value)
            : result.Error.ToProblem();
    }
}
