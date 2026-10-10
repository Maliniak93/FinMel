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

    // Defaulted params, so an omitted one binds instead of failing as missing; 1Y is the chart's default view.
    private static async Task<Results<Ok<NetWorthHistoryResponse>, ProblemHttpResult>> HandleAsync(
        GetNetWorthHistoryHandler handler,
        CancellationToken cancellationToken,
        string range = "1Y",
        Guid? portfolioId = null,
        AssetClass? assetClass = null)
        => (await handler.HandleAsync(range, portfolioId, assetClass, cancellationToken)).ToHttpResult();
}
