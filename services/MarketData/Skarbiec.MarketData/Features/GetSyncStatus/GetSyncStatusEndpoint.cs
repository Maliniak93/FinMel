using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.MarketData.Features.GetSyncStatus;

public static class GetSyncStatusEndpoint
{
    public static IEndpointRouteBuilder MapGetSyncStatusEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/marketdata/sync");

        group.MapGet("/status", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<SyncStatusResponse>, ProblemHttpResult>> HandleAsync(
        GetSyncStatusHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(cancellationToken)).ToHttpResult();
}
