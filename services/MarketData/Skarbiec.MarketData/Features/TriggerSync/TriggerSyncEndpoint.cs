using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.MarketData.Features.TriggerSync;

// Rate limited by the Gateway's standard policy, like every /api/marketdata route.
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
