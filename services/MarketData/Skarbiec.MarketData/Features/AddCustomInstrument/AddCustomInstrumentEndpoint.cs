using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.MarketData.Features.AddCustomInstrument;

public static class AddCustomInstrumentEndpoint
{
    public static IEndpointRouteBuilder MapAddCustomInstrumentEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/marketdata/instruments");

        group.MapPost("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Created<CustomInstrumentResponse>, ProblemHttpResult>> HandleAsync(
        AddCustomInstrumentRequest request, AddCustomInstrumentHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/marketdata/instruments/{result.Value.Id}", result.Value)
            : result.Error.ToProblem();
    }
}
