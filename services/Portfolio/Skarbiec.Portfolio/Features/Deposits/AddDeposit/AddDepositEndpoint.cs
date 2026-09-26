using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Deposits.AddDeposit;

public static class AddDepositEndpoint
{
    public static IEndpointRouteBuilder MapAddDepositEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/portfolios/{portfolioId:guid}/deposits");

        group.MapPost("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Created<DepositResponse>, ProblemHttpResult>> HandleAsync(
        Guid portfolioId, AddDepositRequest request, AddDepositHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(portfolioId, request, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/portfolio/portfolios/{portfolioId}/deposits/{result.Value.AssetId}", result.Value)
            : result.Error.ToProblem();
    }
}
