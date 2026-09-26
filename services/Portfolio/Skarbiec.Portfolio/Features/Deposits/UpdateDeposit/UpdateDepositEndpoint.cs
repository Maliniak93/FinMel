using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Deposits.UpdateDeposit;

public static class UpdateDepositEndpoint
{
    public static IEndpointRouteBuilder MapUpdateDepositEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/portfolios/{portfolioId:guid}/deposits");

        group.MapPut("{assetId:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<DepositResponse>, ProblemHttpResult>> HandleAsync(
        Guid portfolioId, Guid assetId, UpdateDepositRequest request, UpdateDepositHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(portfolioId, assetId, request, cancellationToken)).ToHttpResult();
}
