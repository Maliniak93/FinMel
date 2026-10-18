using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Planning.GetPlanningYear;

public static class GetPlanningYearEndpoint
{
    public static IEndpointRouteBuilder MapGetPlanningYearEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/planning/years");

        group.MapGet("{year:int}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<PlanningYearResponse>, ProblemHttpResult>> HandleAsync(
        int year, GetPlanningYearHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(year, cancellationToken)).ToHttpResult();
}
