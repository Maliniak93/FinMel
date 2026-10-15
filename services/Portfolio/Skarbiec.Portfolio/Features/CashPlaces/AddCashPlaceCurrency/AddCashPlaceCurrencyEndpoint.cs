using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.CashPlaces.AddCashPlaceCurrency;

public static class AddCashPlaceCurrencyEndpoint
{
    public static IEndpointRouteBuilder MapAddCashPlaceCurrencyEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/cash-places");

        group.MapPost("{id:guid}/currencies", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<CashPlaceResponse>, ProblemHttpResult>> HandleAsync(
        Guid id, AddCashPlaceCurrencyRequest request, AddCashPlaceCurrencyHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, request, cancellationToken)).ToHttpResult();
}
