using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.RecordTransaction;

public static class RecordTransactionEndpoint
{
    public static IEndpointRouteBuilder MapRecordTransactionEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/portfolios/{portfolioId:guid}/assets/{assetId:guid}/transactions");

        group.MapPost("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Created<TransactionResponse>, ProblemHttpResult>> HandleAsync(
        Guid portfolioId, Guid assetId, RecordTransactionRequest request, RecordTransactionHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(portfolioId, assetId, request, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/portfolio/portfolios/{portfolioId}/assets/{assetId}/transactions/{result.Value.Id}", result.Value)
            : result.Error.ToProblem();
    }
}
