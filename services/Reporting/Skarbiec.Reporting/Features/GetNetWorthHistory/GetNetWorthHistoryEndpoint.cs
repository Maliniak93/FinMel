using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.Contracts;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Reporting.Features.GetNetWorthHistory;

public static class GetNetWorthHistoryEndpoint
{
    public static IEndpointRouteBuilder MapGetNetWorthHistoryEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reporting/net-worth-history");

        group.MapGet("", HandleAsync).RequireAuthorization();

        return app;
    }

    // Defaulted params, so an omitted one binds instead of failing as missing; no range and no dates is the chart's default 1Y view.
    private static async Task<Results<Ok<NetWorthHistoryResponse>, ProblemHttpResult>> HandleAsync(
        GetNetWorthHistoryHandler handler,
        CancellationToken cancellationToken,
        string? range = null,
        AssetClass? assetClass = null,
        Guid? groupId = null,
        bool ungrouped = false,
        DateOnly? from = null,
        DateOnly? to = null)
        => (await handler.HandleAsync(range, assetClass, cancellationToken, groupId, ungrouped, from, to)).ToHttpResult();
}
