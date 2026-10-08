using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.Http;

namespace Skarbiec.MarketData.Features.GetInstrumentsBatch;

public static class GetInstrumentsBatchEndpoint
{
    public static IEndpointRouteBuilder MapGetInstrumentsBatchEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapInternalGroup("instruments");

        group.MapPost("/batch", HandleAsync);

        return app;
    }

    private static async Task<Ok<InstrumentsBatchResponse>> HandleAsync(
        InstrumentsBatchRequest request, GetInstrumentsBatchHandler handler, CancellationToken cancellationToken)
        => TypedResults.Ok(await handler.HandleAsync(request, cancellationToken));
}
