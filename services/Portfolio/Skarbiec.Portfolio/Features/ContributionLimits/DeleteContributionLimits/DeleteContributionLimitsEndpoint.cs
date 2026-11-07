using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.ContributionLimits.DeleteContributionLimits;

public static class DeleteContributionLimitsEndpoint
{
    public static IEndpointRouteBuilder MapDeleteContributionLimitsEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/contribution-limits");

        group.MapDelete("{year:int}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        int year, DeleteContributionLimitsHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(year, cancellationToken)).ToHttpResult();
}
