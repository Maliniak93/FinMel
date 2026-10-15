using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.CashPlaces.RestoreCashPlace;

public static class RestoreCashPlaceEndpoint
{
    public static IEndpointRouteBuilder MapRestoreCashPlaceEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/cash-places");

        group.MapPost("{id:guid}/restore", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<CashPlaceResponse>, ProblemHttpResult>> HandleAsync(
        Guid id, RestoreCashPlaceHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, cancellationToken)).ToHttpResult();
}
