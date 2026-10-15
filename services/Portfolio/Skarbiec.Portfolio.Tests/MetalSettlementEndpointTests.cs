using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.RecordTransaction;
using Skarbiec.Portfolio.Features.UpdateTransaction;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.CashPlacesApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

// The slice under test (the metal trade and its update and delete) is called directly; fixture helpers only arrange.
public sealed class MetalSettlementEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Buy_FromCashPlace_InvoiceTotal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var safe = await client.CreateFundedCashPlaceAsync(cancellationToken, balance: 2_000m, depositedOn: DefaultTopUpDate);
        var coin = await client.AddMetalAsync(cancellationToken, NewMetalRequest(name: "Gold coin", metal: Metal.Gold));
        var buyDate = new DateOnly(2026, 10, 5);

        var response = await client.PostAsJsonAsync(
            TransactionsUri(coin.AssetId),
            NewMetalCashRequest(safe.AssetId(), pieces: 1m, totalAmount: 1_850.37m, date: buyDate),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1_850.37m, (await response.ReadJsonAsync(cancellationToken)).GetProperty("totalAmount").GetDecimal());
        Assert.Equal(149.63m, (await client.GetAssetAsync(safe.AssetId(), cancellationToken)).Quantity);
        Assert.Equal(1m, (await client.GetAssetAsync(coin.AssetId, cancellationToken)).Quantity);
        await using var dbContext = CreateDbContext(userId);
        var buy = Assert.Single(await dbContext.Transactions.Where(t => t.AssetId == coin.AssetId).ToListAsync(cancellationToken));
        Assert.Equal(TransactionType.Buy, buy.Type);
        Assert.Equal(1_850.37m, buy.UnitPriceAmount);
        Assert.NotNull(buy.TransferId);
        var withdraw = Assert.Single(
            await dbContext.Transactions.Where(t => t.AssetId == safe.AssetId()).ToListAsync(cancellationToken),
            t => t.TransferId == buy.TransferId);
        Assert.Equal(TransactionType.Withdraw, withdraw.Type);
        Assert.Equal(1_850.37m, withdraw.Quantity);
        Assert.Equal(buyDate, withdraw.Date);
    }

    [Fact]
    public async Task SourceRules()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var coin = await client.AddMetalAsync(cancellationToken, NewMetalRequest(name: "Gold coin", metal: Metal.Gold));
        var mBank = await client.CreateBankAccountAsync(cancellationToken, name: "mBank", balance: 5_000m, depositedOn: DefaultTopUpDate);
        var mBankUsd = await client.CreateBankAccountAsync(cancellationToken, name: "mBank USD", currency: "USD");
        var xtb = await client.CreateAccountWithCashAsync(cancellationToken, balance: 5_000m, name: "XTB IKE", depositedOn: DefaultTopUpDate);
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var usdResponse = await client.PostAsJsonAsync(
            TransactionsUri(coin.AssetId), NewMetalCashRequest(mBankUsd.MoneyAssetId, pieces: 1m, totalAmount: 1_000m), cancellationToken);
        await usdResponse.AssertProblemAsync(HttpStatusCode.BadRequest, TransferCurrencyMismatchErrorCode, cancellationToken);

        var xtbResponse = await client.PostAsJsonAsync(
            TransactionsUri(coin.AssetId), NewMetalCashRequest(xtb.CashAssetId, pieces: 1m, totalAmount: 1_000m), cancellationToken);
        await xtbResponse.AssertInvalidTransferCounterpartAsync(cancellationToken);

        var noSourceResponse = await client.PostAsJsonAsync(
            TransactionsUri(coin.AssetId), NewMetalCashRequest(null, pieces: 1m, totalAmount: 1_000m), cancellationToken);
        await noSourceResponse.AssertSettlementSourceRequiredAsync(cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));

        var bankResponse = await client.PostAsJsonAsync(
            TransactionsUri(coin.AssetId), NewMetalCashRequest(mBank.MoneyAssetId, pieces: 1m, totalAmount: 1_000m), cancellationToken);
        Assert.Equal(HttpStatusCode.Created, bankResponse.StatusCode);
        Assert.Equal(4_000m, (await client.GetAssetAsync(mBank.MoneyAssetId, cancellationToken)).Quantity);
    }

    [Theory]
    [InlineData("place")]
    [InlineData("account")]
    public async Task ArchivedSource_ReturnsConflict(string source)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var coin = await client.AddMetalAsync(cancellationToken, NewMetalRequest(name: "Gold coin", metal: Metal.Gold));
        var archivedSource = source == "place"
            ? await client.AddArchivedCashAssetAsync(cancellationToken, name: "Old safe")
            : await client.CreateArchivedBankAccountAsync(cancellationToken);
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.PostAsJsonAsync(
            TransactionsUri(coin.AssetId), NewMetalCashRequest(archivedSource, pieces: 1m, totalAmount: 1_000m), cancellationToken);

        if (source == "place")
        {
            await response.AssertProblemAsync(HttpStatusCode.Conflict, CashPlaceArchivedErrorCode, cancellationToken);
        }
        else
        {
            await response.AssertAccountArchivedConflictAsync(cancellationToken);
        }

        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
    }

    [Fact]
    public async Task InsufficientFunds_Returns400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var safe = await client.CreateFundedCashPlaceAsync(cancellationToken, balance: 1_000m, depositedOn: DefaultTopUpDate);
        var mBank = await client.CreateBankAccountAsync(cancellationToken, balance: 5_000m, depositedOn: DefaultTopUpDate);
        var coin = await client.AddMetalAsync(cancellationToken, NewMetalRequest(name: "Gold coin", metal: Metal.Gold));
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var buy = await client.PostAsJsonAsync(
            TransactionsUri(coin.AssetId), NewMetalCashRequest(safe.AssetId(), pieces: 1m, totalAmount: 1_200m), cancellationToken);

        await buy.AssertInsufficientFundsAsync(cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
        await client.AssertCashUntouchedAsync(safe.AssetId(), cancellationToken, balance: 1_000m);

        await client.RecordMetalTransactionWithCashAsync(
            coin.AssetId, mBank.MoneyAssetId, cancellationToken, pieces: 1m, totalAmount: 1_000m, date: MetalCashDate);
        var proceeds = await client.RecordMetalTransactionWithCashAsync(
            coin.AssetId, safe.AssetId(), cancellationToken,
            type: TransactionType.Sell, pieces: 1m, totalAmount: 1_000m, date: MetalCashDate.AddDays(1));
        await client.RecordTransactionAsync(
            safe.AssetId(), TransactionType.Withdraw, 1_900m, MetalCashDate.AddDays(2), cancellationToken, unitPrice: 1m);
        var beforeDelete = await SnapshotUserRowsAsync(userId, cancellationToken);

        var delete = await client.DeleteAsync(TransactionUri(coin.AssetId, proceeds.Id), cancellationToken);

        await delete.AssertInsufficientFundsAsync(cancellationToken);
        Assert.Equal(beforeDelete, await SnapshotUserRowsAsync(userId, cancellationToken));
        Assert.Equal(100m, (await client.GetAssetAsync(safe.AssetId(), cancellationToken)).Quantity);
        Assert.Equal(0m, (await client.GetAssetAsync(coin.AssetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task TotalRequired()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var safe = await client.CreateFundedCashPlaceAsync(cancellationToken, balance: 2_000m, depositedOn: DefaultTopUpDate);
        var coin = await client.AddMetalAsync(cancellationToken, NewMetalRequest(name: "Gold coin", metal: Metal.Gold));
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);
        var unitPriceOnly = new RecordTransactionRequest
        {
            Type = TransactionType.Buy,
            Quantity = 1m,
            UnitPrice = 1_200m,
            Date = MetalCashDate,
            CashAssetId = safe.AssetId()
        };

        var response = await client.PostAsJsonAsync(TransactionsUri(coin.AssetId), unitPriceOnly, cancellationToken);

        await response.AssertMetalTotalRequiredAsync(cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
    }

    [Fact]
    public async Task Sell_UpdateAndDelete()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateCashAndMetalAsync(cancellationToken);
        await client.RecordMetalTransactionWithCashAsync(
            setup.MetalAssetId, setup.CashAssetId, cancellationToken, pieces: 1m, totalAmount: 1_200m);
        var mBank = await client.CreateBankAccountAsync(cancellationToken, balance: 5_000m, depositedOn: DefaultTopUpDate);

        var sell = await client.RecordMetalTransactionWithCashAsync(
            setup.MetalAssetId, mBank.MoneyAssetId, cancellationToken,
            type: TransactionType.Sell, pieces: 1m, totalAmount: 1_000m, date: MetalCashDate.AddDays(1));
        Assert.Equal(6_000m, (await client.GetAssetAsync(mBank.MoneyAssetId, cancellationToken)).Quantity);

        var update = new UpdateTransactionRequest
        {
            Type = TransactionType.Sell,
            Quantity = 1m,
            TotalAmount = 1_100m,
            CashAssetId = mBank.MoneyAssetId,
            Date = sell.Date
        };
        var updateResponse = await client.PutAsJsonAsync(TransactionUri(setup.MetalAssetId, sell.Id), update, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(6_100m, (await client.GetAssetAsync(mBank.MoneyAssetId, cancellationToken)).Quantity);
        await using (var updatedDb = CreateDbContext(userId))
        {
            Assert.Equal(1_100m, (await updatedDb.Transactions.SingleAsync(t => t.Id == sell.Id, cancellationToken)).UnitPriceAmount);
        }

        var deleteResponse = await client.DeleteAsync(TransactionUri(setup.MetalAssetId, sell.Id), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(5_000m, (await client.GetAssetAsync(mBank.MoneyAssetId, cancellationToken)).Quantity);
        await using var dbContext = CreateDbContext(userId);
        var legs = await dbContext.Transactions.Where(t => t.TransferId != null).ToListAsync(cancellationToken);
        Assert.Equal(2, legs.Count);
        Assert.DoesNotContain(legs, l => l.AssetId == mBank.MoneyAssetId);
    }

    [Fact]
    public async Task CashLeg_UpdateOrDelete_ReturnTransferLegManaged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateCashAndMetalAsync(cancellationToken);
        await client.RecordMetalTransactionWithCashAsync(setup.MetalAssetId, setup.CashAssetId, cancellationToken);
        var withdraw = await client.GetCashWithdrawAsync(setup.CashAssetId, cancellationToken);
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);
        var update = new UpdateTransactionRequest { Type = TransactionType.Withdraw, Quantity = 1_000m, UnitPrice = 1m, Date = withdraw.Date };

        var putResponse = await client.PutAsJsonAsync(TransactionUri(setup.CashAssetId, withdraw.Id), update, cancellationToken);
        await putResponse.AssertTransferLegManagedAsync(cancellationToken);
        var deleteResponse = await client.DeleteAsync(TransactionUri(setup.CashAssetId, withdraw.Id), cancellationToken);
        await deleteResponse.AssertTransferLegManagedAsync(cancellationToken);

        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
    }
}
