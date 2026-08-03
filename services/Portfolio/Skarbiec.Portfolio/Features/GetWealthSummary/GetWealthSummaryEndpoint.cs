using Microsoft.AspNetCore.Http.HttpResults;

namespace Skarbiec.Portfolio.Features.GetWealthSummary;

public static class GetWealthSummaryEndpoint
{
    public static IEndpointRouteBuilder MapGetWealthSummaryEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/wealth-summary");

        group.MapGet("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Ok<WealthSummaryResponse>> HandleAsync(
        GetWealthSummaryHandler handler, CancellationToken cancellationToken)
        => TypedResults.Ok(await handler.HandleAsync(cancellationToken));
}
