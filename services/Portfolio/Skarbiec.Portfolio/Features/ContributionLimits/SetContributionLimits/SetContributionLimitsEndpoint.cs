using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.ContributionLimits.SetContributionLimits;

public static class SetContributionLimitsEndpoint
{
    public static IEndpointRouteBuilder MapSetContributionLimitsEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/contribution-limits");

        group.MapPut("{year:int}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        int year, SetContributionLimitsRequest request, SetContributionLimitsHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(year, request, cancellationToken)).ToHttpResult();
}
