using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Accounts.SetContributionLimit;

public static class SetContributionLimitEndpoint
{
    public static IEndpointRouteBuilder MapSetContributionLimitEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/accounts");

        group.MapPut("{id:guid}/contribution-limits/{year:int}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid id, int year, SetContributionLimitRequest request, SetContributionLimitHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, year, request, cancellationToken)).ToHttpResult();
}
