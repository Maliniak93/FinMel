using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.MarketData.Features.GetInstrument;

public static class GetInstrumentEndpoint
{
    public static IEndpointRouteBuilder MapGetInstrumentEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/marketdata/instruments");

        group.MapGet("/{id:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<InstrumentDetailsResponse>, ProblemHttpResult>> HandleAsync(
        Guid id, GetInstrumentHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, cancellationToken)).ToHttpResult();
}
