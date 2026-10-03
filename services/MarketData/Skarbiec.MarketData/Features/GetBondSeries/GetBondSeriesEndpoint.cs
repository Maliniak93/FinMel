using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.MarketData.Features.GetBondSeries;

public static class GetBondSeriesEndpoint
{
    public static IEndpointRouteBuilder MapGetBondSeriesEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/marketdata/bond-series");

        group.MapGet("/{code}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<BondSeriesResponse>, ProblemHttpResult>> HandleAsync(
        string code, GetBondSeriesHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(code, cancellationToken)).ToHttpResult();
}
