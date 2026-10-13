using System.Net.Http.Json;
using Skarbiec.Portfolio.Features.Watchlist;
using Skarbiec.Portfolio.Features.Watchlist.AddWatchlistItem;

namespace Skarbiec.Portfolio.Tests.Fixtures;

// Arrange only: helpers EnsureSuccessStatusCode, so a test of a watchlist endpoint calls it directly.
internal static class WatchlistApi
{
    public const string WatchlistUri = "/api/portfolio/watchlist";

    public static string WatchlistItemUri(Guid itemId) => $"{WatchlistUri}/{itemId}";

    public static async Task<WatchlistItemResponse> AddWatchlistItemAsync(
        this HttpClient client,
        Guid instrumentId,
        CancellationToken cancellationToken,
        string? note = null,
        decimal? targetPrice = null)
    {
        var response = await client.PostAsJsonAsync(
            WatchlistUri,
            new AddWatchlistItemRequest { InstrumentId = instrumentId, Note = note, TargetPrice = targetPrice },
            cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<WatchlistItemResponse>(cancellationToken))!;
    }

    public static async Task<List<WatchlistItemResponse>> ListWatchlistAsync(
        this HttpClient client, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(WatchlistUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<List<WatchlistItemResponse>>(cancellationToken))!;
    }
}
