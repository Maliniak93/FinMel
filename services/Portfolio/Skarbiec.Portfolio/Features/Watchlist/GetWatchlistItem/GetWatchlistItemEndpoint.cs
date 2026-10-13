using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Watchlist.GetWatchlistItem;

public static class GetWatchlistItemEndpoint
{
    public static IEndpointRouteBuilder MapGetWatchlistItemEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/watchlist");

        group.MapGet("{id:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<WatchlistItemResponse>, ProblemHttpResult>> HandleAsync(
        Guid id, GetWatchlistItemHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, cancellationToken)).ToHttpResult();
}
