using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Watchlist.AddWatchlistItem;

public static class AddWatchlistItemEndpoint
{
    public static IEndpointRouteBuilder MapAddWatchlistItemEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/watchlist");

        group.MapPost("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Created<WatchlistItemResponse>, ProblemHttpResult>> HandleAsync(
        AddWatchlistItemRequest request, AddWatchlistItemHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/portfolio/watchlist/{result.Value.Id}", result.Value)
            : result.Error.ToProblem();
    }
}
