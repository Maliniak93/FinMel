using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;
using Skarbiec.ServiceDefaults.Http;

namespace Skarbiec.MarketData.Features.GetInstrument;

public static class GetInstrumentEndpoint
{
    public static IEndpointRouteBuilder MapGetInstrumentEndpoint(this IEndpointRouteBuilder app)
    {
        // Public route for the SPA (assets list, edit dialog pre-fill, AddCustomInstrument's Location).
        var group = app.MapGroup("/api/marketdata/instruments");

        group.MapGet("/{id:guid}", HandleAsync).RequireAuthorization();

        // Anonymous /internal twin for Portfolio's instrument lookup, which sends no token (ADR-027).
        app.MapInternalGroup("instruments").MapGet("/{id:guid}", HandleAsync);

        return app;
    }

    private static async Task<Results<Ok<InstrumentDetailsResponse>, ProblemHttpResult>> HandleAsync(
        Guid id, GetInstrumentHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, cancellationToken)).ToHttpResult();
}
