using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.SavingsAccounts.GetSavingsAccount;

public static class GetSavingsAccountEndpoint
{
    public static IEndpointRouteBuilder MapGetSavingsAccountEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/portfolios/{portfolioId:guid}/savings-accounts");

        group.MapGet("{assetId:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<SavingsAccountResponse>, ProblemHttpResult>> HandleAsync(
        Guid portfolioId, Guid assetId, GetSavingsAccountHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(portfolioId, assetId, cancellationToken)).ToHttpResult();
}
