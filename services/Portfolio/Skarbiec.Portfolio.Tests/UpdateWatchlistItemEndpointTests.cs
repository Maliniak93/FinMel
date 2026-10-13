using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Watchlist;
using Skarbiec.Portfolio.Features.Watchlist.UpdateWatchlistItem;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.WatchlistApi;

namespace Skarbiec.Portfolio.Tests;

// Every fact calls UpdateWatchlistItem directly.
public sealed class UpdateWatchlistItemEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Update_ReplacesNoteAndTarget()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentId = ScriptInstrument(AssetClass.Etf, "EUR", "VWCE");
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var added = await client.AddWatchlistItemAsync(instrumentId, cancellationToken, note: "core", targetPrice: 100m);

        var response = await client.PutAsJsonAsync(
            WatchlistItemUri(added.Id), new UpdateWatchlistItemRequest { Note = null, TargetPrice = 12.5m }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<WatchlistItemResponse>(cancellationToken))!;
        Assert.Multiple(
            () => Assert.Equal(added.Id, body.Id),
            () => Assert.Null(body.Note),
            () => Assert.Equal(12.5m, body.TargetPrice),
            () => Assert.Equal("VWCE", body.Ticker));
        var stored = Assert.Single(await client.ListWatchlistAsync(cancellationToken));
        Assert.Null(stored.Note);
        Assert.Equal(12.5m, stored.TargetPrice);
    }

    [Fact]
    public async Task Update_UnknownId_ReturnsNotFound()
    {
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PutAsJsonAsync(
            WatchlistItemUri(Guid.NewGuid()), new UpdateWatchlistItemRequest { TargetPrice = 1m }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
