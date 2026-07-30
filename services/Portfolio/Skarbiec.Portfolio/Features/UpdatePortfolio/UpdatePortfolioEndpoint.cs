using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.UpdatePortfolio;

public static class UpdatePortfolioEndpoint
{
    public static IEndpointRouteBuilder MapUpdatePortfolioEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/portfolios");

        group.MapPut("{id:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<PortfolioResponse>, ProblemHttpResult>> HandleAsync(
        Guid id, UpdatePortfolioRequest request, UpdatePortfolioHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, request, cancellationToken)).ToHttpResult();
}
