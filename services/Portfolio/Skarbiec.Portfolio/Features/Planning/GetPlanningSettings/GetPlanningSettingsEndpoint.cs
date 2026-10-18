using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Planning.GetPlanningSettings;

public static class GetPlanningSettingsEndpoint
{
    public static IEndpointRouteBuilder MapGetPlanningSettingsEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/planning/settings");

        group.MapGet("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<PlanningSettingsResponse>, ProblemHttpResult>> HandleAsync(
        GetPlanningSettingsHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(cancellationToken)).ToHttpResult();
}
