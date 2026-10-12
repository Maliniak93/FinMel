using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Accounts.DeleteContributionLimit;

public static class DeleteContributionLimitEndpoint
{
    public static IEndpointRouteBuilder MapDeleteContributionLimitEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/accounts");

        group.MapDelete("{id:guid}/contribution-limits/{year:int}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid id, int year, DeleteContributionLimitHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, year, cancellationToken)).ToHttpResult();
}
