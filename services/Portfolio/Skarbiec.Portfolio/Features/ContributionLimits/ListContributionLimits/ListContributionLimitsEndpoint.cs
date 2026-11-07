using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.ContributionLimits.ListContributionLimits;

public static class ListContributionLimitsEndpoint
{
    public static IEndpointRouteBuilder MapListContributionLimitsEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/contribution-limits");

        group.MapGet("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<List<ContributionLimitsResponse>>, ProblemHttpResult>> HandleAsync(
        ListContributionLimitsHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(cancellationToken)).ToHttpResult();
}
