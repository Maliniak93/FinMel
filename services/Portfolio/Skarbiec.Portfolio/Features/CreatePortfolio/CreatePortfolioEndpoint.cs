using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.CreatePortfolio;

public static class CreatePortfolioEndpoint
{
    public static IEndpointRouteBuilder MapCreatePortfolioEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/portfolios");

        group.MapPost("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Created<PortfolioResponse>, ProblemHttpResult>> HandleAsync(
        CreatePortfolioRequest request, CreatePortfolioHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/portfolio/portfolios/{result.Value.Id}", result.Value)
            : result.Error.ToProblem();
    }
}
