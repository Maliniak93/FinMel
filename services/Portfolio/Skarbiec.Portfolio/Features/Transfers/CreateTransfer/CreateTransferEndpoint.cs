using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Transfers.CreateTransfer;

public static class CreateTransferEndpoint
{
    public static IEndpointRouteBuilder MapCreateTransferEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/transfers");

        group.MapPost("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Created<CreateTransferResponse>, ProblemHttpResult>> HandleAsync(
        CreateTransferRequest request, CreateTransferHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/portfolio/transfers/{result.Value.TransferId}", result.Value)
            : result.Error.ToProblem();
    }
}
