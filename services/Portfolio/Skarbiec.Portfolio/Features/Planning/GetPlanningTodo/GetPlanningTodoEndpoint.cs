using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Planning.GetPlanningTodo;

public static class GetPlanningTodoEndpoint
{
    public static IEndpointRouteBuilder MapGetPlanningTodoEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/planning/todo");

        group.MapGet("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<PlanningTodoResponse>, ProblemHttpResult>> HandleAsync(
        GetPlanningTodoHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(cancellationToken)).ToHttpResult();
}
