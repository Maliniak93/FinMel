using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Planning.GetPlanningMonth;

public static class GetPlanningMonthEndpoint
{
    public static IEndpointRouteBuilder MapGetPlanningMonthEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/planning/months");

        group.MapGet("{month}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<PlanningMonthResponse>, ProblemHttpResult>> HandleAsync(
        string month, GetPlanningMonthHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(month, cancellationToken)).ToHttpResult();
}
