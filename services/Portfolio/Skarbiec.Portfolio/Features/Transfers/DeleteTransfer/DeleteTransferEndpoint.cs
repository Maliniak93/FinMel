using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Transfers.DeleteTransfer;

public static class DeleteTransferEndpoint
{
    public static IEndpointRouteBuilder MapDeleteTransferEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/transfers");

        group.MapDelete("{transferId:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid transferId, DeleteTransferHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(transferId, cancellationToken)).ToHttpResult();
}
