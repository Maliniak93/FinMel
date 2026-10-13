using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Watchlist;
using Skarbiec.Portfolio.Features.Watchlist.AddWatchlistItem;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.WatchlistApi;

namespace Skarbiec.Portfolio.Tests;

// Every fact calls AddWatchlistItem directly; "nothing stored" is read back through the list helper.
public sealed class AddWatchlistItemEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Add_ReturnsCreatedItem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentId = ScriptInstrument(AssetClass.Stock, "USD", "AAPL", 190m, "NASDAQ", "Apple");
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PostAsJsonAsync(
            WatchlistUri,
            new AddWatchlistItemRequest { InstrumentId = instrumentId, Note = "  buy the dip  ", TargetPrice = 150m },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<WatchlistItemResponse>(cancellationToken))!;
        Assert.NotNull(response.Headers.Location);
        Assert.EndsWith(body.Id.ToString(), response.Headers.Location.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Multiple(
            () => Assert.Equal(instrumentId, body.InstrumentId),
            () => Assert.Equal("AAPL", body.Ticker),
            () => Assert.Equal("Apple", body.Name),
            () => Assert.Equal("NASDAQ", body.Exchange),
            () => Assert.Equal(AssetClass.Stock, body.AssetClass),
            () => Assert.Equal("USD", body.QuoteCurrency),
            () => Assert.Equal("buy the dip", body.Note),
            () => Assert.Equal(150m, body.TargetPrice));
        var listed = Assert.Single(await client.ListWatchlistAsync(cancellationToken));
        Assert.Equal(body.Id, listed.Id);
    }

    [Fact]
    public async Task Add_AlreadyWatched_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentId = ScriptInstrument(AssetClass.Etf, "EUR", "VWCE");
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.AddWatchlistItemAsync(instrumentId, cancellationToken);

        var response = await client.PostAsJsonAsync(
            WatchlistUri, new AddWatchlistItemRequest { InstrumentId = instrumentId }, cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, "Conflict.InstrumentAlreadyWatched", cancellationToken);
        Assert.Single(await client.ListWatchlistAsync(cancellationToken));
    }

    [Fact]
    public async Task Add_UnsupportedAssetClass_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentId = ScriptInstrument(AssetClass.Bond, "PLN", "BOND");
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PostAsJsonAsync(
            WatchlistUri, new AddWatchlistItemRequest { InstrumentId = instrumentId }, cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, "Validation.UnsupportedAssetClass", cancellationToken);
        Assert.Empty(await client.ListWatchlistAsync(cancellationToken));
    }

    [Fact]
    public async Task Add_UnknownInstrument_ReturnsInstrumentNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentId = Guid.NewGuid();
        Factory.InstrumentLookupClient.WithNotFound(instrumentId);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PostAsJsonAsync(
            WatchlistUri, new AddWatchlistItemRequest { InstrumentId = instrumentId }, cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, "Validation.InstrumentNotFound", cancellationToken);
        Assert.Empty(await client.ListWatchlistAsync(cancellationToken));
    }

    [Fact]
    public async Task Add_WhenMarketDataUnavailable_ReturnsServiceUnavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentId = Guid.NewGuid();
        Factory.InstrumentLookupClient.WithUnavailable(instrumentId);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PostAsJsonAsync(
            WatchlistUri, new AddWatchlistItemRequest { InstrumentId = instrumentId }, cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.ServiceUnavailable, "ServiceUnavailable.MarketData", cancellationToken);
        Assert.Empty(await client.ListWatchlistAsync(cancellationToken));
    }

    [Fact]
    public async Task Add_WithNoteOver500Characters_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentId = ScriptInstrument(AssetClass.Etf, "EUR", "VWCE");
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PostAsJsonAsync(
            WatchlistUri, new AddWatchlistItemRequest { InstrumentId = instrumentId, Note = new string('n', 501) }, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await client.ListWatchlistAsync(cancellationToken));
    }

    [Fact]
    public async Task Add_WithZeroTargetPrice_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentId = ScriptInstrument(AssetClass.Etf, "EUR", "VWCE");
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PostAsJsonAsync(
            WatchlistUri, new AddWatchlistItemRequest { InstrumentId = instrumentId, TargetPrice = 0m }, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await client.ListWatchlistAsync(cancellationToken));
    }

    [Fact]
    public async Task Add_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            WatchlistUri, new AddWatchlistItemRequest { InstrumentId = Guid.NewGuid() }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
