using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.MarketData.Features.TriggerSync;

/// <summary>
/// Rate limiting (T2.14 scope) is already in place: the Gateway applies its "standard" fixed-window
/// policy (100 req/10s, ADR-013) to every <c>/api/marketdata/{**catch-all}</c> route, this one
/// included — no extra per-endpoint limiter needed here.
/// </summary>
public static class TriggerSyncEndpoint
{
    public static IEndpointRouteBuilder MapTriggerSyncEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/marketdata/sync");

        group.MapPost("/trigger", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        TriggerSyncHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(cancellationToken)).ToHttpResult();
}
