using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Watchlist.UpdateWatchlistItem;

public static class UpdateWatchlistItemEndpoint
{
    public static IEndpointRouteBuilder MapUpdateWatchlistItemEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/watchlist");

        group.MapPut("{id:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<WatchlistItemResponse>, ProblemHttpResult>> HandleAsync(
        Guid id, UpdateWatchlistItemRequest request, UpdateWatchlistItemHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, request, cancellationToken)).ToHttpResult();
}
