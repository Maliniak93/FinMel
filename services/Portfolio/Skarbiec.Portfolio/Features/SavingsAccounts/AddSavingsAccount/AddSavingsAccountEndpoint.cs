using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.SavingsAccounts.AddSavingsAccount;

public static class AddSavingsAccountEndpoint
{
    public static IEndpointRouteBuilder MapAddSavingsAccountEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/portfolios/{portfolioId:guid}/savings-accounts");

        group.MapPost("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Created<SavingsAccountResponse>, ProblemHttpResult>> HandleAsync(
        Guid portfolioId, AddSavingsAccountRequest request, AddSavingsAccountHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(portfolioId, request, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/portfolio/portfolios/{portfolioId}/savings-accounts/{result.Value.AssetId}", result.Value)
            : result.Error.ToProblem();
    }
}
