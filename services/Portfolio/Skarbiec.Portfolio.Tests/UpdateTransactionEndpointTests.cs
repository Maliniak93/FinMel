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

    /// <summary>cash-transaction-types AC-4: editing a Cash asset's Deposit into a Sell is a 400
    /// <c>Validation.TransactionTypeNotAllowed</c>, and the stored transaction and the balance are
    /// exactly as before.</summary>
    [Fact]
    public async Task Update_ToDisallowedTypeOnCashAsset_ReturnsBadRequestAndNothingChanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var assetId = await client.AddCashAssetAsync(portfolioId, cancellationToken);
        await client.RecordTransactionAsync(
            portfolioId, assetId, TransactionType.Deposit, 1_000m, new DateOnly(2026, 1, 1), cancellationToken, unitPrice: 1m);
        var depositId = await client.RecordTransactionAsync(
            portfolioId, assetId, TransactionType.Deposit, 500m, new DateOnly(2026, 1, 2), cancellationToken, unitPrice: 1m);
        // A Sell of 100 after the opening 1000 would never oversell — only the type rule can reject it.
        var update = new UpdateTransactionRequest { Type = TransactionType.Sell, Quantity = 100m, UnitPrice = 1m, Date = new DateOnly(2026, 1, 2) };

        var response = await client.PutAsJsonAsync(TransactionUri(portfolioId, assetId, depositId), update, cancellationToken);

        await response.AssertTransactionTypeNotAllowedAsync(cancellationToken);
        var stored = (await client.ListTransactionsAsync(portfolioId, assetId, cancellationToken)).Items.Single(t => t.Id == depositId);
        Assert.Equal(TransactionType.Deposit, stored.Type);
        Assert.Equal(500m, stored.Quantity);
        Assert.Equal(1m, stored.UnitPrice);
        Assert.Equal(new DateOnly(2026, 1, 2), stored.Date);
        Assert.Equal(1_500m, (await client.GetAssetAsync(portfolioId, assetId, cancellationToken)).Quantity);
    }

    /// <summary>
    /// savings-accounts AC-7: unlike a term deposit's, a savings account's transactions stay editable —
    /// changing the opening Deposit moves the balance, and turning it into a Buy is refused.
    /// </summary>
    [Fact]
    public async Task Update_SavingsAccountTransaction_MovesBalance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken);
        var openingId = Assert.Single((await client.ListTransactionsAsync(portfolioId, account.AssetId, cancellationToken)).Items).Id;
        var edit = new UpdateTransactionRequest { Type = TransactionType.Deposit, Quantity = 12_000m, UnitPrice = 1m, Date = SavingsToday };

        var response = await client.PutAsJsonAsync(TransactionUri(portfolioId, account.AssetId, openingId), edit, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(12_000m, (await client.GetAssetAsync(portfolioId, account.AssetId, cancellationToken)).Quantity);
        Assert.Equal(12_000m, (await client.GetSavingsAccountAsync(portfolioId, account.AssetId, cancellationToken)).Balance);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(portfolioId, account.AssetId, cancellationToken);

        var toBuy = edit with { Type = TransactionType.Buy };
        var refused = await client.PutAsJsonAsync(TransactionUri(portfolioId, account.AssetId, openingId), toBuy, cancellationToken);

        await refused.AssertTransactionTypeNotAllowedAsync(cancellationToken);
        Assert.Equal(12_000m, (await client.GetAssetAsync(portfolioId, account.AssetId, cancellationToken)).Quantity);
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

    /// <summary>asset-archive AC-4: editing a transaction of an archived asset is a 409 <c>Conflict.AssetArchived</c>; the transaction stays.</summary>
    [Fact]
    public async Task Update_ArchivedAsset_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var cashId = await client.AddCashAssetAsync(portfolioId, cancellationToken);
        var depositId = await client.RecordTransactionAsync(
            portfolioId, cashId, TransactionType.Deposit, 100m, new DateOnly(2026, 1, 1), cancellationToken, unitPrice: 1m);
        await client.ArchiveAssetAsync(portfolioId, cashId, cancellationToken);
        var update = new UpdateTransactionRequest { Type = TransactionType.Deposit, Quantity = 150m, UnitPrice = 1m, Date = new DateOnly(2026, 1, 1) };

        var response = await client.PutAsJsonAsync(TransactionUri(portfolioId, cashId, depositId), update, cancellationToken);

        await response.AssertAssetArchivedConflictAsync(cancellationToken);
        Assert.Equal(100m, (await client.GetAssetAsync(portfolioId, cashId, cancellationToken)).Quantity);
        Assert.Equal(100m, (await client.ListTransactionsAsync(portfolioId, cashId, cancellationToken)).Items.Single(t => t.Id == depositId).Quantity);
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

    /// <summary>
    /// asset-transfers-deposit-funding AC-6 (update half): the Cash Withdraw leg of a transfer is
    /// changed only by its entry point — a PUT here is a 409 <c>Conflict.TransferLegManaged</c> and
    /// nothing changes, while the ordinary top-up on the same Cash asset stays editable.
    /// </summary>
    [Fact]
    public async Task Update_TransferLeg_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var funded = await client.CreateFundedDepositAsync(cancellationToken);
        var leg = await client.GetCashWithdrawAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken);
        var legUpdate = new UpdateTransactionRequest { Type = TransactionType.Withdraw, Quantity = 200m, UnitPrice = 1m, Date = leg.Date };

        var response = await client.PutAsJsonAsync(
            TransactionUri(funded.CashPortfolioId, funded.CashAssetId, leg.Id), legUpdate, cancellationToken);

        await response.AssertTransferLegManagedAsync(cancellationToken);
        var unchanged = await client.GetCashWithdrawAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken);
        Assert.Equal(leg.Id, unchanged.Id);
        Assert.Equal(1_000m, unchanged.Quantity);
        Assert.NotNull(unchanged.Transfer);
        Assert.Equal(4_000m, (await client.GetAssetAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken)).Quantity);
        Assert.Equal(1_000m, (await client.GetAssetAsync(funded.DepositPortfolioId, funded.Deposit.AssetId, cancellationToken)).Quantity);

        // Control: the plain top-up beside it is still an ordinary, editable transaction.
        var topUp = Assert.Single(
            (await client.ListTransactionsAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken)).Items,
            t => t.Type == TransactionType.Deposit);
        var topUpUpdate = new UpdateTransactionRequest { Type = TransactionType.Deposit, Quantity = 6_000m, UnitPrice = 1m, Date = topUp.Date };
        var topUpResponse = await client.PutAsJsonAsync(
            TransactionUri(funded.CashPortfolioId, funded.CashAssetId, topUp.Id), topUpUpdate, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, topUpResponse.StatusCode);
        Assert.Equal(5_000m, (await client.GetAssetAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken)).Quantity);
    }

    /// <summary>term-deposits AC-10: the opening transaction of a term deposit is rewritten only
    /// through <c>PUT .../deposits/{id}</c> — editing it here is a 409
    /// <c>Conflict.DepositTransactionsManaged</c> and nothing changes.</summary>
    [Fact]
    public async Task Update_OnTermDeposit_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        var openingId = Assert.Single((await client.ListTransactionsAsync(portfolioId, deposit.AssetId, cancellationToken)).Items).Id;
        var update = new UpdateTransactionRequest { Type = TransactionType.Deposit, Quantity = 20_000m, UnitPrice = 1m, Date = new DateOnly(2026, 1, 15) };

        var response = await client.PutAsJsonAsync(TransactionUri(portfolioId, deposit.AssetId, openingId), update, cancellationToken);

        await response.AssertDepositTransactionsManagedAsync(cancellationToken);
        Assert.Equal(10_000m, (await client.GetAssetAsync(portfolioId, deposit.AssetId, cancellationToken)).Quantity);
        var opening = Assert.Single((await client.ListTransactionsAsync(portfolioId, deposit.AssetId, cancellationToken)).Items);
        Assert.Equal(openingId, opening.Id);
        Assert.Equal(10_000m, opening.Quantity);
    }

    /// <summary>
    /// savings-interest-settlement AC-9: the credit a settlement created is system-managed - editing
    /// it is a 409 <c>Conflict.SavingsInterestManaged</c> and nothing changes - while an ordinary
    /// savings Deposit beside it stays editable.
    /// </summary>
    [Fact]
    public async Task Update_SavingsInterestCredit_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken, NewInterestAccountRequest());
        await client.SettlePreviewedSavingsInterestAsync(portfolioId, account.AssetId, cancellationToken);
        var transactions = (await client.ListTransactionsAsync(portfolioId, account.AssetId, cancellationToken)).Items;
        var credit = Assert.Single(transactions, t => t.SavingsInterestPeriodEnd is not null);
        var opening = Assert.Single(transactions, t => t.SavingsInterestPeriodEnd is null);
        var update = new UpdateTransactionRequest { Type = TransactionType.Deposit, Quantity = 99m, UnitPrice = 1m, Date = credit.Date };

        var response = await client.PutAsJsonAsync(TransactionUri(portfolioId, account.AssetId, credit.Id), update, cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.SavingsInterestManagedErrorCode, cancellationToken);
        var unchanged = Assert.Single(
            (await client.ListTransactionsAsync(portfolioId, account.AssetId, cancellationToken)).Items, t => t.Id == credit.Id);
        Assert.Equal(33.29m, unchanged.Quantity);
        Assert.Equal(10_033.29m, (await client.GetAssetAsync(portfolioId, account.AssetId, cancellationToken)).Quantity);

        // Control: the ordinary opening Deposit is still editable.
        var openingUpdate = new UpdateTransactionRequest { Type = TransactionType.Deposit, Quantity = 12_000m, UnitPrice = 1m, Date = opening.Date };
        var openingResponse = await client.PutAsJsonAsync(TransactionUri(portfolioId, account.AssetId, opening.Id), openingUpdate, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, openingResponse.StatusCode);
        Assert.Equal(12_033.29m, (await client.GetAssetAsync(portfolioId, account.AssetId, cancellationToken)).Quantity);
    }

    /// <summary>savings-interest-settlement: an edit inside an already-settled period leaves that settlement as it was.</summary>
    [Fact]
    public async Task Update_TransactionInSettledPeriod_LeavesSettlementAsItIs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken, NewInterestAccountRequest());
        await client.SettlePreviewedSavingsInterestAsync(portfolioId, account.AssetId, cancellationToken);
        var settlementId = await client.GetLastSettlementIdAsync(portfolioId, account.AssetId, cancellationToken);
        var opening = Assert.Single(
            (await client.ListTransactionsAsync(portfolioId, account.AssetId, cancellationToken)).Items,
            t => t.SavingsInterestPeriodEnd is null);
        var update = new UpdateTransactionRequest { Type = TransactionType.Deposit, Quantity = 12_000m, UnitPrice = 1m, Date = opening.Date };

        var response = await client.PutAsJsonAsync(TransactionUri(portfolioId, account.AssetId, opening.Id), update, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var last = (await client.GetSavingsAccountAsync(portfolioId, account.AssetId, cancellationToken)).LastSettlement!;
        Assert.Equal(settlementId, last.SettlementId);
        Assert.Equal(41.10m, last.GrossInterest);
        Assert.Equal(7.81m, last.Tax);
        Assert.Equal(33.29m, last.NetInterest);
    }

    /// <summary>savings-cash-transfers AC-5: a leg of a manual Cash/Savings transfer is changed only through /transfers.</summary>
    [Fact]
    public async Task Update_ManualTransferLeg_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var setup = await client.CreateCashAndSavingsAsync(cancellationToken);
        await client.CreateTransferAsync(cancellationToken, NewTransferRequest(setup.CashAssetId, setup.SavingsAssetId, 2_000m));
        var leg = await client.GetCashWithdrawAsync(setup.CashPortfolioId, setup.CashAssetId, cancellationToken);
        var update = new UpdateTransactionRequest { Type = TransactionType.Withdraw, Quantity = 200m, UnitPrice = 1m, Date = leg.Date };

        var response = await client.PutAsJsonAsync(
            TransactionUri(setup.CashPortfolioId, setup.CashAssetId, leg.Id), update, cancellationToken);

        await response.AssertTransferLegManagedAsync(cancellationToken);
        var unchanged = await client.GetCashWithdrawAsync(setup.CashPortfolioId, setup.CashAssetId, cancellationToken);
        Assert.Equal(2_000m, unchanged.Quantity);
        Assert.NotNull(unchanged.Transfer);
        Assert.Equal(3_000m, (await client.GetAssetAsync(setup.CashPortfolioId, setup.CashAssetId, cancellationToken)).Quantity);
        Assert.Equal(2_000m, (await client.GetAssetAsync(setup.SavingsPortfolioId, setup.SavingsAssetId, cancellationToken)).Quantity);
    }
}
