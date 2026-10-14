using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Accounts.BankInterest.SettleBankInterest;

public static class SettleBankInterestEndpoint
{
    public static IEndpointRouteBuilder MapSettleBankInterestEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/accounts");

        group.MapPost("{id:guid}/interest-settlements", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Created<BankInterestSettlementResponse>, ProblemHttpResult>> HandleAsync(
        Guid id,
        SettleBankInterestRequest request,
        SettleBankInterestHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, request, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created(
                $"/api/portfolio/accounts/{id}/interest-settlements/{result.Value.SettlementId}",
                result.Value)
            : result.Error.ToProblem();
    }
}
