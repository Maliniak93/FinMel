using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.GetPortfolio;

public static class GetPortfolioEndpoint
{
    public static IEndpointRouteBuilder MapGetPortfolioEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/portfolios");

        group.MapGet("{id:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<PortfolioResponse>, ProblemHttpResult>> HandleAsync(
        Guid id, GetPortfolioHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, cancellationToken)).ToHttpResult();
}
