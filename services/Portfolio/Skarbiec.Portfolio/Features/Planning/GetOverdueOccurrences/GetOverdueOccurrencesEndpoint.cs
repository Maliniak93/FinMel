using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Planning.GetOverdueOccurrences;

public static class GetOverdueOccurrencesEndpoint
{
    public static IEndpointRouteBuilder MapGetOverdueOccurrencesEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/planning/overdue");

        group.MapGet("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<OverdueOccurrencesResponse>, ProblemHttpResult>> HandleAsync(
        GetOverdueOccurrencesHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(cancellationToken)).ToHttpResult();
}
