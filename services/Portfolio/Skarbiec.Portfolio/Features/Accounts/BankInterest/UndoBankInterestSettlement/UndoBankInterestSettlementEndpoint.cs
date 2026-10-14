using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Accounts.BankInterest.UndoBankInterestSettlement;

public static class UndoBankInterestSettlementEndpoint
{
    public static IEndpointRouteBuilder MapUndoBankInterestSettlementEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/accounts");

        group.MapDelete("{id:guid}/interest-settlements/{settlementId:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid id,
        Guid settlementId,
        UndoBankInterestSettlementHandler handler,
        CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, settlementId, cancellationToken)).ToHttpResult();
}
