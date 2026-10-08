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

    private static async Task<Results<Created<CustomInstrumentResponse>, Ok<CustomInstrumentResponse>, ProblemHttpResult>> HandleAsync(
        AddCustomInstrumentRequest request, AddCustomInstrumentHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        var instrument = result.Value.Instrument;
        return result.Value.Created
            ? TypedResults.Created($"/api/marketdata/instruments/{instrument.Id}", instrument)
            : TypedResults.Ok(instrument);
    }
}
