using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.RecurringIncomes.Occurrences.SkipOccurrence;

public static class SkipOccurrenceEndpoint
{
    public static IEndpointRouteBuilder MapSkipOccurrenceEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/recurring-incomes");

        group.MapPut("{id:guid}/occurrences/{month}/skip", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid id, string month, SkipOccurrenceHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, month, cancellationToken)).ToHttpResult();
}
