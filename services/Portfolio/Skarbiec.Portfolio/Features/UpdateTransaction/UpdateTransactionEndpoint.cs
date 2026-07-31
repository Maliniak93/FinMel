using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.UpdateTransaction;

public static class UpdateTransactionEndpoint
{
    public static IEndpointRouteBuilder MapUpdateTransactionEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/portfolios/{portfolioId:guid}/assets/{assetId:guid}/transactions");

        group.MapPut("{id:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<TransactionResponse>, ProblemHttpResult>> HandleAsync(
        Guid portfolioId, Guid assetId, Guid id, UpdateTransactionRequest request, UpdateTransactionHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(portfolioId, assetId, id, request, cancellationToken)).ToHttpResult();
}
