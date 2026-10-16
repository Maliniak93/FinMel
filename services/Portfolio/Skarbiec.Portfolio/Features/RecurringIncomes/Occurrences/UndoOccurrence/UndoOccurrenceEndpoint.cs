using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.RecurringIncomes.Occurrences.UndoOccurrence;

public static class UndoOccurrenceEndpoint
{
    public static IEndpointRouteBuilder MapUndoOccurrenceEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/recurring-incomes");

        group.MapDelete("{id:guid}/occurrences/{month}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid id, string month, UndoOccurrenceHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, month, cancellationToken)).ToHttpResult();
}
