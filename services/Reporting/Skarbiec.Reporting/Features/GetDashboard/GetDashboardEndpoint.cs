using Microsoft.AspNetCore.Http.HttpResults;

namespace Skarbiec.Reporting.Features.GetDashboard;

public static class GetDashboardEndpoint
{
    public static IEndpointRouteBuilder MapGetDashboardEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reporting/dashboard");

        group.MapGet("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Ok<DashboardResponse>> HandleAsync(
        GetDashboardHandler handler, CancellationToken cancellationToken, Guid? portfolioId = null)
        => TypedResults.Ok(await handler.HandleAsync(portfolioId, cancellationToken));
}
