using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.SavingsAccounts.UpdateSavingsAccount;

public static class UpdateSavingsAccountEndpoint
{
    public static IEndpointRouteBuilder MapUpdateSavingsAccountEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/savings-accounts");

        group.MapPut("{assetId:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<SavingsAccountResponse>, ProblemHttpResult>> HandleAsync(
        Guid assetId, UpdateSavingsAccountRequest request, UpdateSavingsAccountHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(assetId, request, cancellationToken)).ToHttpResult();
}
