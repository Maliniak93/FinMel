using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.RecurringIncomes.Occurrences.SettleOccurrence;

public static class SettleOccurrenceEndpoint
{
    public static IEndpointRouteBuilder MapSettleOccurrenceEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/recurring-incomes");

        group.MapPut("{id:guid}/occurrences/{month}/settlement", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid id, string month, SettleOccurrenceRequest request, SettleOccurrenceHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, month, request, cancellationToken)).ToHttpResult();
}
