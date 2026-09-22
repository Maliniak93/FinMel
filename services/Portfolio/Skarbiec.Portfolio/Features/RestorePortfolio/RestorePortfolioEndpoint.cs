using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.RestorePortfolio;

public static class RestorePortfolioEndpoint
{
    public static IEndpointRouteBuilder MapRestorePortfolioEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/portfolios");

        group.MapPost("{id:guid}/restore", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<PortfolioResponse>, ProblemHttpResult>> HandleAsync(
        Guid id, RestorePortfolioHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, cancellationToken)).ToHttpResult();
}
