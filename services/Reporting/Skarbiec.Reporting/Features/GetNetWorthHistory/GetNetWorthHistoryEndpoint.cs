using Microsoft.AspNetCore.Http.HttpResults;
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

    // Defaulted range (mirrors ListTransactionsEndpoint's page/pageSize) so an omitted query
    // parameter binds instead of 400ing as missing-required; "1Y" is the chart's default view.
    private static async Task<Results<Ok<NetWorthHistoryResponse>, ProblemHttpResult>> HandleAsync(
        GetNetWorthHistoryHandler handler,
        CancellationToken cancellationToken,
        string range = "1Y",
        Guid? portfolioId = null)
        => (await handler.HandleAsync(range, portfolioId, cancellationToken)).ToHttpResult();
}
