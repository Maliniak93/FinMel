using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.RecurringExpenses.Payments.UndoExpensePayment;

public static class UndoExpensePaymentEndpoint
{
    public static IEndpointRouteBuilder MapUndoExpensePaymentEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/recurring-expenses");

        group.MapDelete("{id:guid}/payments/{month}/settlement", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid id, string month, UndoExpensePaymentHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, month, cancellationToken)).ToHttpResult();
}
