using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.Authentication;

namespace Skarbiec.Portfolio.Features.GetPositionsForValuation;

public static class GetPositionsForValuationEndpoint
{
    public static IEndpointRouteBuilder MapGetPositionsForValuationEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/positions-for-valuation");

        group.MapGet("", HandleAsync).RequireAuthorization(SystemCaller.PolicyName);

        return app;
    }

    private static async Task<Ok<PositionsForValuationPage>> HandleAsync(
        GetPositionsForValuationHandler handler, CancellationToken cancellationToken, int page = 1, int? pageSize = null)
        => TypedResults.Ok(await handler.HandleAsync(page, pageSize, cancellationToken));
}
