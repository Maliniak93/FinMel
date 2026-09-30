using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.SavingsAccounts.UndoSavingsInterestSettlement;

public static class UndoSavingsInterestSettlementEndpoint
{
    public static IEndpointRouteBuilder MapUndoSavingsInterestSettlementEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/portfolios/{portfolioId:guid}/savings-accounts");

        group.MapDelete("{assetId:guid}/interest-settlements/{settlementId:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid portfolioId,
        Guid assetId,
        Guid settlementId,
        UndoSavingsInterestSettlementHandler handler,
        CancellationToken cancellationToken)
        => (await handler.HandleAsync(portfolioId, assetId, settlementId, cancellationToken)).ToHttpResult();
}
