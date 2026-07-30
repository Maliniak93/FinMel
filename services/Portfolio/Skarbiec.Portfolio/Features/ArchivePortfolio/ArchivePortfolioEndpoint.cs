using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.ArchivePortfolio;

public static class ArchivePortfolioEndpoint
{
    public static IEndpointRouteBuilder MapArchivePortfolioEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/portfolios");

        group.MapPost("{id:guid}/archive", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<PortfolioResponse>, ProblemHttpResult>> HandleAsync(
        Guid id, ArchivePortfolioHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, cancellationToken)).ToHttpResult();
}
