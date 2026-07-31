using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.DeleteTransaction;

public static class DeleteTransactionEndpoint
{
    public static IEndpointRouteBuilder MapDeleteTransactionEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/portfolios/{portfolioId:guid}/assets/{assetId:guid}/transactions");

        group.MapDelete("{id:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid portfolioId, Guid assetId, Guid id, DeleteTransactionHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(portfolioId, assetId, id, cancellationToken)).ToHttpResult();
}
