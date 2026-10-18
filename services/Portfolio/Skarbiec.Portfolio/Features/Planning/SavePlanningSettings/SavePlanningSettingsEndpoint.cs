using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Planning.SavePlanningSettings;

public static class SavePlanningSettingsEndpoint
{
    public static IEndpointRouteBuilder MapSavePlanningSettingsEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/planning/settings");

        group.MapPut("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        SavePlanningSettingsRequest request, SavePlanningSettingsHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(request, cancellationToken)).ToHttpResult();
}
