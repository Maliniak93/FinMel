using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Watchlist;
using Skarbiec.Portfolio.Features.Watchlist.UpdateWatchlistItem;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;
using static Skarbiec.Portfolio.Tests.Fixtures.WatchlistApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class WatchlistTenancyIsolationTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Get_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var itemId = await AddOwnersItemAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await stranger.GetAsync(WatchlistItemUri(itemId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var itemId = await AddOwnersItemAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await stranger.PutAsJsonAsync(
            WatchlistItemUri(itemId), new UpdateWatchlistItemRequest { TargetPrice = 1m }, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var itemId = await AddOwnersItemAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await stranger.DeleteAsync(WatchlistItemUri(itemId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_ForStranger_DoesNotIncludeResource()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await AddOwnersItemAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await stranger.GetAsync(WatchlistUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<List<WatchlistItemResponse>>(cancellationToken))!);
    }

    [Fact]
    public async Task List_ForeignHoldings_Absent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        var instrumentId = ScriptInstrument(AssetClass.Etf, "PLN", "SHARED");
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await owner.AddWatchlistItemAsync(instrumentId, cancellationToken);
        var ownerAccountId = await owner.CreateAccountAsync(cancellationToken, "Owner account");
        await owner.FundAccountAsync(ownerAccountId, cancellationToken);
        var ownerAsset = await AddQuotedHoldingAsync(
            owner, ownerAccountId, cancellationToken, AssetClass.Etf, "PLN", "Owner shares", instrumentId: instrumentId);
        await owner.RecordTransactionAsync(ownerAsset, TransactionType.Buy, 5m, new DateOnly(2026, 1, 5), cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await stranger.AddWatchlistItemAsync(instrumentId, cancellationToken);

        var response = await stranger.GetAsync(WatchlistUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await response.Content.ReadFromJsonAsync<List<WatchlistItemResponse>>(cancellationToken))!);
        Assert.Empty(item.Holdings);
        Assert.Equal(0m, item.HeldQuantity);
    }

    private async Task<Guid> AddOwnersItemAsync(CancellationToken cancellationToken)
    {
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var added = await owner.AddWatchlistItemAsync(ScriptInstrument(AssetClass.Etf, "EUR", "VWCE"), cancellationToken);

        return added.Id;
    }
}
