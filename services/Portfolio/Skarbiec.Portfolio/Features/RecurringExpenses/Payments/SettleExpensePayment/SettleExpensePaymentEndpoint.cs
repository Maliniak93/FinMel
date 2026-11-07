using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.RecurringExpenses.Payments.SettleExpensePayment;

public static class SettleExpensePaymentEndpoint
{
    public static IEndpointRouteBuilder MapSettleExpensePaymentEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/recurring-expenses");

        group.MapPut("{id:guid}/payments/{month}/settlement", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid id, string month, SettleExpensePaymentRequest request, SettleExpensePaymentHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, month, request, cancellationToken)).ToHttpResult();
}
