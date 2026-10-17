using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.TransferPlan.SaveTransferPlanSettings;

public static class SaveTransferPlanSettingsEndpoint
{
    public static IEndpointRouteBuilder MapSaveTransferPlanSettingsEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/transfer-plans");

        group.MapPut("settings", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        SaveTransferPlanSettingsRequest request, SaveTransferPlanSettingsHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(request, cancellationToken)).ToHttpResult();
}
