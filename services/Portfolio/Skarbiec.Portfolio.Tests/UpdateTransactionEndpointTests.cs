using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.UpdateTransaction;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class UpdateTransactionEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Update_ChangesQuantityAndRecomputesAsset()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        var buyId = await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 10m, new DateOnly(2026, 1, 1), cancellationToken);
        var update = new UpdateTransactionRequest { Type = TransactionType.Buy, Quantity = 15m, UnitPrice = 100m, Date = new DateOnly(2026, 1, 1) };

        var response = await client.PutAsJsonAsync(TransactionUri(portfolioId, assetId, buyId), update, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TransactionResponse>(cancellationToken);
        Assert.Equal(15m, body!.Quantity);
        Assert.Equal(100m, body.UnitPrice);
        Assert.Equal(15m, (await client.GetAssetAsync(portfolioId, assetId, cancellationToken)).Quantity);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(portfolioId, assetId, cancellationToken);
    }

    [Fact]
    public async Task Update_OldBuyDownwardThatBreaksLaterSell_ReturnsConflictAndNothingChanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        var buyId = await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 10m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Sell, 8m, new DateOnly(2026, 1, 2), cancellationToken);
        // 10 - 8 = 2 today; dropping the Buy to 5 would let the Sell dip the running total to -3.
        var update = new UpdateTransactionRequest { Type = TransactionType.Buy, Quantity = 5m, UnitPrice = 100m, Date = new DateOnly(2026, 1, 1) };

        var response = await client.PutAsJsonAsync(TransactionUri(portfolioId, assetId, buyId), update, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(2m, (await client.GetAssetAsync(portfolioId, assetId, cancellationToken)).Quantity);
        var page = await client.ListTransactionsAsync(portfolioId, assetId, cancellationToken);
        var untouchedBuy = page.Items.Single(t => t.Id == buyId);
        Assert.Equal(10m, untouchedBuy.Quantity);
    }

    [Fact]
    public async Task Update_ForNonExistentTransaction_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        var update = new UpdateTransactionRequest { Type = TransactionType.Buy, Quantity = 1m, UnitPrice = 1m, Date = new DateOnly(2026, 1, 1) };

        var response = await client.PutAsJsonAsync(TransactionUri(portfolioId, assetId, Guid.NewGuid()), update, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_AssetUnderWrongPortfolio_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        var buyId = await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 10m, new DateOnly(2026, 1, 1), cancellationToken);
        var otherPortfolioId = await client.CreatePortfolioAsync(cancellationToken, name: "Other portfolio");
        var update = new UpdateTransactionRequest { Type = TransactionType.Buy, Quantity = 1m, UnitPrice = 1m, Date = new DateOnly(2026, 1, 1) };

        var response = await client.PutAsJsonAsync(TransactionUri(otherPortfolioId, assetId, buyId), update, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithoutToken_ReturnsUnauthorized()
    {
        var update = new UpdateTransactionRequest { Type = TransactionType.Buy, Quantity = 1m, UnitPrice = 1m, Date = new DateOnly(2026, 1, 1) };
        using var client = Factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            TransactionUri(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), update, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>transactions-pln-value-and-fee-removal AC7: an update re-resolves the rate for the
    /// (new) date — a transaction stored at 4.30 is revalued at 4.50 once moved to that rate's date.</summary>
    [Fact]
    public async Task Update_ChangedDate_ReResolvesRate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var originalDate = new DateOnly(2026, 3, 2);
        var newDate = new DateOnly(2026, 3, 10);
        Factory.FxRateLookupClient
            .WithRate("EUR", originalDate, 4.30m)
            .WithRate("EUR", newDate, 4.50m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken, currency: "EUR");
        var buyId = await client.RecordTransactionAsync(
            portfolioId, assetId, TransactionType.Buy, 10m, originalDate, cancellationToken, unitPrice: 100m);
        Assert.Equal(4300.00m, (await client.ListTransactionsAsync(portfolioId, assetId, cancellationToken)).Items.Single().ValuePln);
        var update = new UpdateTransactionRequest { Type = TransactionType.Buy, Quantity = 10m, UnitPrice = 100m, Date = newDate };

        var response = await client.PutAsJsonAsync(TransactionUri(portfolioId, assetId, buyId), update, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TransactionResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal("EUR", body.Currency);
        Assert.Equal(4500.00m, body.ValuePln);
        Assert.Equal(("EUR", newDate), Factory.FxRateLookupClient.Calls[^1]);
        Assert.Equal(4500.00m, (await client.ListTransactionsAsync(portfolioId, assetId, cancellationToken)).Items.Single().ValuePln);
    }

    /// <summary>transactions-pln-value-and-fee-removal AC10: editing a transaction of an archived
    /// portfolio's asset is a 409 before any rate lookup — MarketData is never asked.</summary>
    [Fact]
    public async Task Update_OnArchivedPortfolio_ReturnsConflictWithoutFxLookup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.FxRateLookupClient.WithRate("EUR", 4.30m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken, currency: "EUR");
        var buyId = await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 10m, new DateOnly(2026, 3, 2), cancellationToken);
        await client.ArchivePortfolioAsync(portfolioId, cancellationToken);
        var callsBeforeUpdate = Factory.FxRateLookupClient.Calls.Count;
        var update = new UpdateTransactionRequest { Type = TransactionType.Buy, Quantity = 15m, UnitPrice = 100m, Date = new DateOnly(2026, 3, 10) };

        var response = await client.PutAsJsonAsync(TransactionUri(portfolioId, assetId, buyId), update, cancellationToken);

        await response.AssertPortfolioArchivedConflictAsync(cancellationToken);
        Assert.Equal(callsBeforeUpdate, Factory.FxRateLookupClient.Calls.Count);
        var unchanged = (await client.ListTransactionsAsync(portfolioId, assetId, cancellationToken)).Items.Single();
        Assert.Equal(10m, unchanged.Quantity);
        Assert.Equal(new DateOnly(2026, 3, 2), unchanged.Date);
    }

    /// <summary>archived-portfolio-out-of-net-worth AC7: editing a transaction of an archived
    /// portfolio's asset is a 409 <c>Conflict.PortfolioArchived</c>; transaction and quantity stay.</summary>
    [Fact]
    public async Task Update_InArchivedPortfolio_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        var buyId = await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 10m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.ArchivePortfolioAsync(portfolioId, cancellationToken);
        var update = new UpdateTransactionRequest { Type = TransactionType.Buy, Quantity = 15m, UnitPrice = 100m, Date = new DateOnly(2026, 1, 1) };

        var response = await client.PutAsJsonAsync(TransactionUri(portfolioId, assetId, buyId), update, cancellationToken);

        await response.AssertPortfolioArchivedConflictAsync(cancellationToken);
        Assert.Equal(10m, (await client.GetAssetAsync(portfolioId, assetId, cancellationToken)).Quantity);
        var page = await client.ListTransactionsAsync(portfolioId, assetId, cancellationToken);
        Assert.Equal(10m, page.Items.Single(t => t.Id == buyId).Quantity);
    }
}
