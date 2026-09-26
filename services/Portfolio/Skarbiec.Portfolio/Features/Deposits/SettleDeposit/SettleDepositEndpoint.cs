using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Deposits.SettleDeposit;

public static class SettleDepositEndpoint
{
    public static IEndpointRouteBuilder MapSettleDepositEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/portfolios/{portfolioId:guid}/deposits");

        group.MapPost("{assetId:guid}/settle", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<DepositResponse>, ProblemHttpResult>> HandleAsync(
        Guid portfolioId, Guid assetId, SettleDepositRequest request, SettleDepositHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(portfolioId, assetId, request, cancellationToken)).ToHttpResult();
}
