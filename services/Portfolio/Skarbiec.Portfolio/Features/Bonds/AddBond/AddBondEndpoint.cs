using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Bonds.AddBond;

public static class AddBondEndpoint
{
    public static IEndpointRouteBuilder MapAddBondEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/portfolios/{portfolioId:guid}/bonds");

        group.MapPost("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Created<BondResponse>, ProblemHttpResult>> HandleAsync(
        Guid portfolioId, AddBondRequest request, AddBondHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(portfolioId, request, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/portfolio/portfolios/{portfolioId}/bonds/{result.Value.AssetId}", result.Value)
            : result.Error.ToProblem();
    }
}
