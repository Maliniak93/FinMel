using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.Deposits.SettleDeposit;
using Skarbiec.Portfolio.Features.Transfers;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class SettleDepositEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Settle_WithPreviewValues_CreditsNetInterestAndMarksSettled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var deposit = await client.AddDepositAsync(cancellationToken);
        var openingId = Assert.Single((await client.ListTransactionsAsync(deposit.AssetId, cancellationToken)).Items).Id;
        var previewResponse = await client.GetAsync(DepositSettlementPreviewUri(deposit.AssetId), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = await previewResponse.ReadJsonAsync(cancellationToken);
        var request = new SettleDepositRequest
        {
            SettledOn = DateOnly.Parse(preview.GetProperty("settledOn").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
            GrossInterest = preview.GetProperty("grossInterest").GetDecimal(),
            Tax = preview.GetProperty("tax").GetDecimal()
        };

        var response = await client.PostAsJsonAsync(SettleDepositUri(deposit.AssetId), request, cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Settle answered {(int)response.StatusCode}.");

        var transactions = (await client.ListTransactionsAsync(deposit.AssetId, cancellationToken)).Items;
        Assert.Equal(2, transactions.Count);
        var credit = Assert.Single(transactions, t => t.Id != openingId);
        Assert.Equal(TransactionType.Deposit, credit.Type);
        Assert.Equal(119.83m, credit.Quantity);
        Assert.Equal(1m, credit.UnitPrice);
        Assert.Equal(new DateOnly(2026, 4, 15), credit.Date);

        var asset = await client.GetAssetAsync(deposit.AssetId, cancellationToken);
        Assert.Equal(10_119.83m, asset.Quantity);
        Assert.Equal(AssetClass.Deposit, asset.AssetClass);
        Assert.True(asset.DepositSettled);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(deposit.AssetId, cancellationToken);

        var settled = await client.GetDepositAsync(deposit.AssetId, cancellationToken);
        Assert.Equal(DepositStatus.Settled, settled.Status);
        Assert.Equal(new DateOnly(2026, 4, 15), settled.SettledOn);
        Assert.Equal(147.95m, settled.SettledGrossInterest);
        Assert.Equal(28.12m, settled.SettledTax);
    }

    [Fact]
    public async Task Settle_WithOverriddenAmounts_UsesThem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var deposit = await client.AddDepositAsync(cancellationToken);
        var request = NewSettleRequest(settledOn: new DateOnly(2026, 4, 17), grossInterest: 150.00m, tax: 28.50m);

        var response = await client.PostAsJsonAsync(SettleDepositUri(deposit.AssetId), request, cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Settle answered {(int)response.StatusCode}.");

        var settled = await client.GetDepositAsync(deposit.AssetId, cancellationToken);
        Assert.Equal(DepositStatus.Settled, settled.Status);
        Assert.Equal(new DateOnly(2026, 4, 17), settled.SettledOn);
        Assert.Equal(150.00m, settled.SettledGrossInterest);
        Assert.Equal(28.50m, settled.SettledTax);

        var asset = await client.GetAssetAsync(deposit.AssetId, cancellationToken);
        Assert.Equal(10_121.50m, asset.Quantity);

        var credit = Assert.Single(
            (await client.ListTransactionsAsync(deposit.AssetId, cancellationToken)).Items,
            t => t.Date == new DateOnly(2026, 4, 17));
        Assert.Equal(TransactionType.Deposit, credit.Type);
        Assert.Equal(121.50m, credit.Quantity);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(deposit.AssetId, cancellationToken);
    }

    [Fact]
    public async Task Settle_ZeroNetInterest_AddsNoTransactionAndMarksSettled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var deposit = await client.AddDepositAsync(cancellationToken);

        var response = await client.PostAsJsonAsync(
            SettleDepositUri(deposit.AssetId), NewSettleRequest(grossInterest: 0m, tax: 0m), cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Settle answered {(int)response.StatusCode}.");
        var asset = await client.GetAssetAsync(deposit.AssetId, cancellationToken);
        Assert.Equal(10_000m, asset.Quantity);
        Assert.Equal(1, asset.TransactionCount);
        Assert.True(asset.DepositSettled);
        var settled = await client.GetDepositAsync(deposit.AssetId, cancellationToken);
        Assert.Equal(DepositStatus.Settled, settled.Status);
        Assert.Equal(0m, settled.SettledGrossInterest);
        Assert.Equal(0m, settled.SettledTax);
    }

    [Fact]
    public async Task Settle_ForeignCurrencyDeposit_FreezesFxRateOfSettlementDate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        Factory.FxRateLookupClient
            .WithRate("EUR", new DateOnly(2026, 1, 15), 4.30m)
            .WithRate("EUR", new DateOnly(2026, 4, 17), 4.25m);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var deposit = await client.AddDepositAsync(cancellationToken, NewDepositRequest(currency: "EUR"));

        var response = await client.PostAsJsonAsync(
            SettleDepositUri(deposit.AssetId), NewSettleRequest(settledOn: new DateOnly(2026, 4, 17)), cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Settle answered {(int)response.StatusCode}.");
        await using var dbContext = CreateDbContext(userId);
        var transactions = await dbContext.Transactions
            .Where(t => t.AssetId == deposit.AssetId)
            .OrderBy(t => t.Date)
            .ToListAsync(cancellationToken);
        Assert.Equal(2, transactions.Count);
        Assert.Equal(4.30m, transactions[0].FxRateToPln);
        Assert.Equal(new DateOnly(2026, 4, 17), transactions[1].Date);
        Assert.Equal(119.83m, transactions[1].Quantity);
        Assert.Equal(4.25m, transactions[1].FxRateToPln);
    }

    [Fact]
    public async Task Settle_BeforeMaturity_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Warsaw 2026-04-14 12:00 — the day before the 2026-04-15 maturity.
        Factory.Clock.SetUtcNow(new DateTimeOffset(2026, 4, 14, 10, 0, 0, TimeSpan.Zero));
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var deposit = await client.AddDepositAsync(cancellationToken);
        // settledOn is today and after the start: valid on its own, so only the maturity rule can reject it.
        var request = NewSettleRequest(settledOn: new DateOnly(2026, 4, 14));

        var response = await client.PostAsJsonAsync(SettleDepositUri(deposit.AssetId), request, cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.DepositNotDueErrorCode, cancellationToken);
        await client.AssertDepositUnsettledAsync(deposit.AssetId, cancellationToken);
    }

    [Theory]
    [InlineData("tax-over-gross")]
    [InlineData("gross-negative")]
    [InlineData("tax-negative")]
    [InlineData("settled-on-in-future")]
    [InlineData("settled-on-before-start")]
    public async Task Settle_InvalidInput_ReturnsBadRequest(string invalidCase)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Today is Warsaw 2026-04-20; the deposit started 2026-01-15.
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var deposit = await client.AddDepositAsync(cancellationToken);
        var request = invalidCase switch
        {
            "tax-over-gross" => NewSettleRequest(grossInterest: 147.95m, tax: 147.96m),
            "gross-negative" => NewSettleRequest(grossInterest: -1m, tax: 0m),
            "tax-negative" => NewSettleRequest(tax: -0.01m),
            "settled-on-in-future" => NewSettleRequest(settledOn: new DateOnly(2026, 4, 21)),
            "settled-on-before-start" => NewSettleRequest(settledOn: new DateOnly(2026, 1, 14)),
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase), invalidCase, null)
        };

        var response = await client.PostAsJsonAsync(SettleDepositUri(deposit.AssetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await client.AssertDepositUnsettledAsync(deposit.AssetId, cancellationToken);
    }

    [Fact]
    public async Task Settle_Twice_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var deposit = await client.AddDepositAsync(cancellationToken);
        await client.SettleDepositAsync(deposit.AssetId, cancellationToken);

        var response = await client.PostAsJsonAsync(
            SettleDepositUri(deposit.AssetId),
            NewSettleRequest(settledOn: new DateOnly(2026, 4, 18), grossInterest: 200m, tax: 38m),
            cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.DepositAlreadySettledErrorCode, cancellationToken);
        var asset = await client.GetAssetAsync(deposit.AssetId, cancellationToken);
        Assert.Equal(10_119.83m, asset.Quantity);
        Assert.Equal(2, asset.TransactionCount);
        var settled = await client.GetDepositAsync(deposit.AssetId, cancellationToken);
        Assert.Equal(new DateOnly(2026, 4, 15), settled.SettledOn);
        Assert.Equal(147.95m, settled.SettledGrossInterest);
        Assert.Equal(28.12m, settled.SettledTax);
    }

    [Fact]
    public async Task Settle_ArchivedDeposit_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var deposit = await client.AddDepositAsync(cancellationToken);
        await client.ArchiveAssetAsync(deposit.AssetId, cancellationToken);

        var response = await client.PostAsJsonAsync(SettleDepositUri(deposit.AssetId), NewSettleRequest(), cancellationToken);

        await response.AssertAssetArchivedConflictAsync(cancellationToken);
        await client.AssertDepositUnsettledAsync(deposit.AssetId, cancellationToken);
    }

    [Fact]
    public async Task Settle_ToArchivedCash_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var deposit = await client.AddDepositAsync(cancellationToken);
        var cashId = await client.AddArchivedCashAssetAsync(cancellationToken);
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.PostAsJsonAsync(
            SettleDepositUri(deposit.AssetId), NewSettleRequest(destinationAssetId: cashId), cancellationToken);

        await response.AssertInvalidTransferCounterpartAsync(cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
        await client.AssertDepositUnsettledAsync(deposit.AssetId, cancellationToken);
        await client.AssertCashUntouchedAsync(cashId, cancellationToken);
    }

    [Fact]
    public async Task Settle_NonDepositAsset_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var _ = await client.AddDepositAsync(cancellationToken);
        var cashId = await client.AddCashAssetAsync(cancellationToken);

        var response = await client.PostAsJsonAsync(SettleDepositUri(cashId), NewSettleRequest(), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var cash = await client.GetAssetAsync(cashId, cancellationToken);
        Assert.Equal(0m, cash.Quantity);
        Assert.Equal(0, cash.TransactionCount);
        Assert.Null(cash.DepositSettled);
    }

    [Fact]
    public async Task Settle_WithDestination_PaysOutWholeBalance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var deposit = await client.AddDepositAsync(cancellationToken);
        var cashId = await client.AddCashAssetAsync(cancellationToken);
        var settledOn = new DateOnly(2026, 4, 15);

        var response = await client.PostAsJsonAsync(
            SettleDepositUri(deposit.AssetId), NewSettleRequest(destinationAssetId: cashId), cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Settle answered {(int)response.StatusCode}.");

        var depositTransactions = (await client.ListTransactionsAsync(deposit.AssetId, cancellationToken)).Items;
        Assert.Equal(3, depositTransactions.Count);
        var credit = Assert.Single(depositTransactions, t => t.Type == TransactionType.Deposit && t.Date == settledOn);
        Assert.Equal(119.83m, credit.Quantity);
        Assert.Null(credit.Transfer);
        var withdraw = Assert.Single(depositTransactions, t => t.Type == TransactionType.Withdraw);
        Assert.Equal(10_119.83m, withdraw.Quantity);
        Assert.Equal(settledOn, withdraw.Date);
        Assert.NotNull(withdraw.Transfer);
        Assert.Equal(cashId, withdraw.Transfer.CounterpartAssetId);
        Assert.Equal(TransferDirection.Out, withdraw.Transfer.Direction);

        var cashLeg = Assert.Single((await client.ListTransactionsAsync(cashId, cancellationToken)).Items);
        Assert.Equal(TransactionType.Deposit, cashLeg.Type);
        Assert.Equal(10_119.83m, cashLeg.Quantity);
        Assert.Equal(settledOn, cashLeg.Date);
        Assert.NotNull(cashLeg.Transfer);
        Assert.Equal(deposit.AssetId, cashLeg.Transfer.CounterpartAssetId);
        Assert.Equal(TransferDirection.In, cashLeg.Transfer.Direction);

        await using (var dbContext = CreateDbContext(userId))
        {
            var legs = await dbContext.Transactions.Where(t => t.TransferId != null).ToListAsync(cancellationToken);
            Assert.Equal(2, legs.Count);
            Assert.Single(legs.Select(t => t.TransferId).Distinct());
        }

        Assert.Equal(0m, (await client.GetAssetAsync(deposit.AssetId, cancellationToken)).Quantity);
        Assert.Equal(10_119.83m, (await client.GetAssetAsync(cashId, cancellationToken)).Quantity);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(deposit.AssetId, cancellationToken);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(cashId, cancellationToken);

        var paidOut = await client.GetDepositAsync(deposit.AssetId, cancellationToken);
        Assert.Equal(DepositStatus.PaidOut, paidOut.Status);
        Assert.Equal(settledOn, paidOut.SettledOn);
        Assert.Equal(147.95m, paidOut.SettledGrossInterest);
        Assert.Equal(28.12m, paidOut.SettledTax);
        Assert.Equal(settledOn, paidOut.PaidOutOn);
        Assert.Equal("Cash account", paidOut.PaidOutToAssetName);
    }

    [Theory]
    [InlineData("eur-cash")]
    [InlineData("stock")]
    [InlineData("archived-cash")]
    [InlineData("strangers-cash")]
    [InlineData("eur-savings")]
    [InlineData("archived-savings")]
    [InlineData("strangers-savings")]
    public async Task Settle_InvalidDestination_ReturnsBadRequestAndSettlesNothing(string invalidCase)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var deposit = await client.AddDepositAsync(cancellationToken);
        var (destinationClient, destinationId) = invalidCase switch
        {
            "eur-cash" => (client, await client.AddCashAssetAsync(cancellationToken, currency: "EUR", name: "EUR account")),
            "stock" => (client, await client.AddAssetAsync(await client.CreateAccountAsync(cancellationToken), cancellationToken, name: "Some stock")),
            "archived-cash" => (client, await client.AddArchivedCashAssetAsync(cancellationToken)),
            "strangers-cash" => (stranger, await stranger.AddCashAssetAsync(cancellationToken)),
            "eur-savings" => (
                client,
                (await client.AddSavingsAccountAsync(cancellationToken, NewSavingsAccountRequest(currency: "EUR", withOpeningDeposit: false))).AssetId),
            "archived-savings" => (client, await client.AddArchivedSavingsAccountAsync(cancellationToken)),
            "strangers-savings" => (
                stranger,
                (await stranger.AddSavingsAccountAsync(cancellationToken, NewSavingsAccountRequest(withOpeningDeposit: false))).AssetId),
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase), invalidCase, null)
        };

        var response = await client.PostAsJsonAsync(
            SettleDepositUri(deposit.AssetId), NewSettleRequest(destinationAssetId: destinationId), cancellationToken);

        await response.AssertInvalidTransferCounterpartAsync(cancellationToken);
        await client.AssertDepositUnsettledAsync(deposit.AssetId, cancellationToken);
        var destination = await destinationClient.GetAssetAsync(destinationId, cancellationToken);
        Assert.Equal(invalidCase == "archived-cash" ? 5_000m : 0m, destination.Quantity);
        Assert.Equal(invalidCase == "archived-cash" ? 1 : 0, destination.TransactionCount);
    }

    [Fact]
    public async Task Settle_WithSavingsDestination_PaysOutWholeBalance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var deposit = await client.AddDepositAsync(cancellationToken);
        var account = await client.AddSavingsAccountAsync(
            cancellationToken, NewSavingsAccountRequest(withOpeningDeposit: false));
        var settledOn = new DateOnly(2026, 4, 15);

        var response = await client.PostAsJsonAsync(
            SettleDepositUri(deposit.AssetId), NewSettleRequest(destinationAssetId: account.AssetId), cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Settle answered {(int)response.StatusCode}.");

        var withdraw = Assert.Single(
            (await client.ListTransactionsAsync(deposit.AssetId, cancellationToken)).Items,
            t => t.Type == TransactionType.Withdraw);
        Assert.Equal(10_119.83m, withdraw.Quantity);
        Assert.Equal(settledOn, withdraw.Date);
        Assert.NotNull(withdraw.Transfer);
        Assert.Equal(account.AssetId, withdraw.Transfer.CounterpartAssetId);
        Assert.Equal(TransferDirection.Out, withdraw.Transfer.Direction);

        var savingsLeg = Assert.Single((await client.ListTransactionsAsync(account.AssetId, cancellationToken)).Items);
        Assert.Equal(TransactionType.Deposit, savingsLeg.Type);
        Assert.Equal(10_119.83m, savingsLeg.Quantity);
        Assert.Equal(settledOn, savingsLeg.Date);
        Assert.NotNull(savingsLeg.Transfer);
        Assert.Equal(deposit.AssetId, savingsLeg.Transfer.CounterpartAssetId);
        Assert.Equal(TransferDirection.In, savingsLeg.Transfer.Direction);
        Assert.Equal(withdraw.Transfer.TransferId, savingsLeg.Transfer.TransferId);

        Assert.Equal(0m, (await client.GetAssetAsync(deposit.AssetId, cancellationToken)).Quantity);
        Assert.Equal(10_119.83m, (await client.GetAssetAsync(account.AssetId, cancellationToken)).Quantity);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(deposit.AssetId, cancellationToken);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(account.AssetId, cancellationToken);

        var paidOut = await client.GetDepositAsync(deposit.AssetId, cancellationToken);
        Assert.Equal(DepositStatus.PaidOut, paidOut.Status);
        Assert.Equal(settledOn, paidOut.PaidOutOn);
        Assert.Equal(account.Name, paidOut.PaidOutToAssetName);
    }
}
