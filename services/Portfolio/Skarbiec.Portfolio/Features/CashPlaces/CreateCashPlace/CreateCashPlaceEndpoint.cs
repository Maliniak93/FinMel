using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.CashPlaces.CreateCashPlace;

public static class CreateCashPlaceEndpoint
{
    public static IEndpointRouteBuilder MapCreateCashPlaceEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/cash-places");

        group.MapPost("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Created<CashPlaceResponse>, ProblemHttpResult>> HandleAsync(
        CreateCashPlaceRequest request, CreateCashPlaceHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/portfolio/cash-places/{result.Value.Id}", result.Value)
            : result.Error.ToProblem();
    }
}
