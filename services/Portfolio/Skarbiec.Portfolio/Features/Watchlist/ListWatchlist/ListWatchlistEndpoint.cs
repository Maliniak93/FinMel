using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.Contracts;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Watchlist.ListWatchlist;

public static class ListWatchlistEndpoint
{
    public static IEndpointRouteBuilder MapListWatchlistEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/watchlist");

        group.MapGet("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<IReadOnlyList<WatchlistItemResponse>>, ProblemHttpResult>> HandleAsync(
        AssetClass? assetClass, ListWatchlistHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(assetClass, cancellationToken)).ToHttpResult();
}
