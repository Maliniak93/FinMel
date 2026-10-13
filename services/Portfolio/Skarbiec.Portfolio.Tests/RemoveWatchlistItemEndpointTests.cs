using System.Net;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.WatchlistApi;

namespace Skarbiec.Portfolio.Tests;

// Every fact calls RemoveWatchlistItem directly.
public sealed class RemoveWatchlistItemEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Remove_DeletesItemAndAllowsReAdd()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentId = ScriptInstrument(AssetClass.Etf, "EUR", "VWCE");
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var added = await client.AddWatchlistItemAsync(instrumentId, cancellationToken);

        var response = await client.DeleteAsync(WatchlistItemUri(added.Id), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await client.ListWatchlistAsync(cancellationToken));
        var readded = await client.AddWatchlistItemAsync(instrumentId, cancellationToken);
        Assert.NotEqual(added.Id, readded.Id);
    }

    [Fact]
    public async Task Remove_UnknownId_ReturnsNotFound()
    {
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.DeleteAsync(WatchlistItemUri(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
