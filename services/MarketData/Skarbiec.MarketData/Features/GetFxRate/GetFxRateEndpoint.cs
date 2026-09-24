using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;
using Skarbiec.ServiceDefaults.Http;

namespace Skarbiec.MarketData.Features.GetFxRate;

public static class GetFxRateEndpoint
{
    public static IEndpointRouteBuilder MapGetFxRateEndpoint(this IEndpointRouteBuilder app)
    {
        // Service-only (ADR-026, ADR-027): Portfolio's request-path lookup of the rate a transaction is
        // frozen at. Anonymous, not in OpenAPI, unreachable through the Gateway.
        app.MapInternalGroup("fx").MapGet("/{currency}/rate", HandleAsync);

        return app;
    }

    private static async Task<Results<Ok<FxRateResponse>, ProblemHttpResult>> HandleAsync(
        string currency, DateOnly date, GetFxRateHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(currency, date, cancellationToken)).ToHttpResult();
}
