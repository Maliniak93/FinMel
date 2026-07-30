using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.DeletePortfolio;

public static class DeletePortfolioEndpoint
{
    public static IEndpointRouteBuilder MapDeletePortfolioEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/portfolios");

        group.MapDelete("{id:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid id, DeletePortfolioHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, cancellationToken)).ToHttpResult();
}
