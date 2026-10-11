using System.Net;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class UndoSavingsInterestSettlementEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Undo_Latest_RemovesCreditAndReopensPeriod()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(OctoberEndedUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken, NewInterestAccountRequest());
        await client.SettlePreviewedSavingsInterestAsync(account.AssetId, cancellationToken);
        var septemberSettlementId = await client.GetLastSettlementIdAsync(account.AssetId, cancellationToken);
        await client.SettlePreviewedSavingsInterestAsync(account.AssetId, cancellationToken);
        var octoberSettlementId = await client.GetLastSettlementIdAsync(account.AssetId, cancellationToken);
        Assert.NotEqual(septemberSettlementId, octoberSettlementId);
        Assert.Equal(10_067.80m, (await client.GetSavingsAccountAsync(account.AssetId, cancellationToken)).Balance);

        var response = await client.DeleteAsync(
            SavingsInterestSettlementUri(account.AssetId, octoberSettlementId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var afterUndo = await client.GetSavingsAccountAsync(account.AssetId, cancellationToken);
        Assert.Equal(10_033.29m, afterUndo.Balance);
        Assert.Equal(septemberSettlementId, afterUndo.LastSettlement!.SettlementId);
        var transactions = (await client.ListTransactionsAsync(account.AssetId, cancellationToken)).Items;
        Assert.Equal(2, transactions.Count);
        Assert.DoesNotContain(transactions, t => t.SavingsInterestPeriodEnd == OctoberEnd);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(account.AssetId, cancellationToken);
        Assert.Equal(1, await CountSavingsSettlementsAsync(userId, cancellationToken, account.AssetId));

        var preview = await client.GetSavingsInterestPreviewAsync(account.AssetId, cancellationToken);
        Assert.Equal("2026-10-31", preview.GetProperty("periodEnd").GetString());
    }

    [Fact]
    public async Task Undo_NotLatest_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(OctoberEndedUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken, NewInterestAccountRequest());
        await client.SettlePreviewedSavingsInterestAsync(account.AssetId, cancellationToken);
        var septemberSettlementId = await client.GetLastSettlementIdAsync(account.AssetId, cancellationToken);
        await client.SettlePreviewedSavingsInterestAsync(account.AssetId, cancellationToken);

        var response = await client.DeleteAsync(
            SavingsInterestSettlementUri(account.AssetId, septemberSettlementId), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.SavingsSettlementNotLatestErrorCode, cancellationToken);
        Assert.Equal(2, await CountSavingsSettlementsAsync(userId, cancellationToken, account.AssetId));
        Assert.Equal(10_067.80m, (await client.GetSavingsAccountAsync(account.AssetId, cancellationToken)).Balance);
        Assert.Equal(3, (await client.ListTransactionsAsync(account.AssetId, cancellationToken)).TotalCount);
    }

    [Fact]
    public async Task Undo_CreditAlreadySpent_IsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken, NewInterestAccountRequest());
        await client.SettlePreviewedSavingsInterestAsync(account.AssetId, cancellationToken);
        var settlementId = await client.GetLastSettlementIdAsync(account.AssetId, cancellationToken);
        await client.RecordTransactionAsync(
            account.AssetId, TransactionType.Withdraw, 10_033.29m, new DateOnly(2026, 10, 1), cancellationToken, unitPrice: 1m);

        var response = await client.DeleteAsync(SavingsInterestSettlementUri(account.AssetId, settlementId), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.OversellsPositionErrorCode, cancellationToken);
        Assert.Equal(1, await CountSavingsSettlementsAsync(userId, cancellationToken, account.AssetId));
        Assert.Equal(0m, (await client.GetSavingsAccountAsync(account.AssetId, cancellationToken)).Balance);
        Assert.Contains(
            (await client.ListTransactionsAsync(account.AssetId, cancellationToken)).Items,
            t => t.SavingsInterestPeriodEnd == SeptemberEnd);
    }

    [Fact]
    public async Task Undo_ZeroNetSettlement_RemovesItAndKeepsTheBalance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken, NewInterestAccountRequest());
        await client.SettleSavingsInterestAsync(account.AssetId, SeptemberEnd, 10.00m, 10.00m, cancellationToken);
        var settlementId = await client.GetLastSettlementIdAsync(account.AssetId, cancellationToken);

        var response = await client.DeleteAsync(SavingsInterestSettlementUri(account.AssetId, settlementId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, await CountSavingsSettlementsAsync(userId, cancellationToken, account.AssetId));
        var asset = await client.GetAssetAsync(account.AssetId, cancellationToken);
        Assert.Equal(10_000m, asset.Quantity);
        Assert.Equal(1, asset.TransactionCount);
        var preview = await client.GetSavingsInterestPreviewAsync(account.AssetId, cancellationToken);
        Assert.Equal("2026-09-30", preview.GetProperty("periodEnd").GetString());
    }

    [Fact]
    public async Task Undo_UnknownSettlement_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken, NewInterestAccountRequest());

        var response = await client.DeleteAsync(SavingsInterestSettlementUri(account.AssetId, Guid.NewGuid()), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Undo_InArchivedPortfolio_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken, NewInterestAccountRequest());
        await client.SettlePreviewedSavingsInterestAsync(account.AssetId, cancellationToken);
        var settlementId = await client.GetLastSettlementIdAsync(account.AssetId, cancellationToken);
        await client.ArchivePortfolioAsync(portfolioId, cancellationToken);

        var response = await client.DeleteAsync(SavingsInterestSettlementUri(account.AssetId, settlementId), cancellationToken);

        await response.AssertPortfolioArchivedConflictAsync(cancellationToken);
        Assert.Equal(1, await CountSavingsSettlementsAsync(userId, cancellationToken, account.AssetId));
        Assert.Equal(10_033.29m, (await client.GetAssetAsync(account.AssetId, cancellationToken)).Quantity);
    }
}
