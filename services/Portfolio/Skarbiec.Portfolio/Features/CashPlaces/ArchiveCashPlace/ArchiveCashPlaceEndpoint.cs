using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.CashPlaces.ArchiveCashPlace;

public static class ArchiveCashPlaceEndpoint
{
    public static IEndpointRouteBuilder MapArchiveCashPlaceEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/cash-places");

        group.MapPost("{id:guid}/archive", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<CashPlaceResponse>, ProblemHttpResult>> HandleAsync(
        Guid id, ArchiveCashPlaceHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, cancellationToken)).ToHttpResult();
}
