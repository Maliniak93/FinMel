using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Watchlist;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;
using static Skarbiec.Portfolio.Tests.Fixtures.WatchlistApi;

namespace Skarbiec.Portfolio.Tests;

// Every fact calls GetWatchlistItem directly.
public sealed class GetWatchlistItemEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Get_ReturnsTheListEntry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(FixedTodayUtc);
        Factory.InstrumentQuoteLookupClient.WithRate("USD", 4.00m);
        var instrumentId = ScriptInstrument(AssetClass.Etf, "USD", "ETFA", 110m, "NYSE", "Alpha ETF", previousClose: 100m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var added = await client.AddWatchlistItemAsync(instrumentId, cancellationToken, note: "core", targetPrice: 120m);

        var response = await client.GetAsync(WatchlistItemUri(added.Id), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<WatchlistItemResponse>(cancellationToken))!;
        var listed = Assert.Single(await client.ListWatchlistAsync(cancellationToken));
        Assert.Equal(JsonSerializer.Serialize(listed), JsonSerializer.Serialize(body));
        Assert.Equal(added.Id, body.Id);
    }

    [Fact]
    public async Task Get_UnknownId_ReturnsNotFound()
    {
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync(WatchlistItemUri(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
