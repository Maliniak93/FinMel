using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.CashPlaces.GetCashPlace;

public static class GetCashPlaceEndpoint
{
    public static IEndpointRouteBuilder MapGetCashPlaceEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/cash-places");

        group.MapGet("{id:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<CashPlaceResponse>, ProblemHttpResult>> HandleAsync(
        Guid id, GetCashPlaceHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, cancellationToken)).ToHttpResult();
}
