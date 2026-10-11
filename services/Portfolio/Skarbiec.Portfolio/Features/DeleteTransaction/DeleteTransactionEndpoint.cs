using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.DeleteTransaction;

public static class DeleteTransactionEndpoint
{
    public static IEndpointRouteBuilder MapDeleteTransactionEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/assets/{assetId:guid}/transactions");

        group.MapDelete("{id:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid assetId, Guid id, DeleteTransactionHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(assetId, id, cancellationToken)).ToHttpResult();
}
