using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.CashPlaces.UpdateCashPlace;

public static class UpdateCashPlaceEndpoint
{
    public static IEndpointRouteBuilder MapUpdateCashPlaceEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/cash-places");

        group.MapPut("{id:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<CashPlaceResponse>, ProblemHttpResult>> HandleAsync(
        Guid id, UpdateCashPlaceRequest request, UpdateCashPlaceHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, request, cancellationToken)).ToHttpResult();
}
