using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.Metals.AddMetal;
using Skarbiec.Portfolio.Features.RecordTransaction;
using Skarbiec.Portfolio.Features.Transfers.CreateTransfer;
using Skarbiec.Portfolio.Features.Transfers.DeleteTransfer;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

public sealed class TransferOutboxTests(SkarbiecContainersFixture containers) : PortfolioOutboxTestBase(containers)
{
    [Fact]
    public async Task CreateTransfer_PublishesPositionChangedForBothAssets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var (mBankId, nestAssetId) = await ArrangeTwoBankAccountsAsync(cancellationToken);
        var positionEventsBefore = await CountPositionEventsAsync(cancellationToken);
        SaveChanges.Reset();

        await using (var act = Provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<CreateTransferHandler>()
                .HandleAsync(PortfolioApi.NewTransferRequest(mBankId, nestAssetId, 2_000m), cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, SaveChanges.Count);

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var events = (await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(positionEventsBefore).ToList();
        Assert.Equal(2, events.Count);
        var mBankEvent = Assert.Single(events, e => e.AssetId == mBankId);
        Assert.Equal(3_000m, mBankEvent.Quantity);
        Assert.NotNull(mBankEvent.AccountId);
        Assert.Equal(UserId, mBankEvent.UserId);
        var nestEvent = Assert.Single(events, e => e.AssetId == nestAssetId);
        Assert.Equal(2_000m, nestEvent.Quantity);
        Assert.NotNull(nestEvent.AccountId);
        Assert.Equal(AssetClass.BankAccount, nestEvent.AssetClass);

        var legs = await verifyDb.Transactions.Where(t => t.TransferId != null).ToListAsync(cancellationToken);
        Assert.Equal(2, legs.Count);
        Assert.Single(legs.Select(t => t.TransferId).Distinct());
    }

    [Fact]
    public async Task DeleteTransfer_PublishesPositionChangedForBothAssets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var (mBankId, nestAssetId) = await ArrangeTwoBankAccountsAsync(cancellationToken);
        Guid transferId;
        await using (var create = Provider.CreateAsyncScope())
        {
            var created = await create.ServiceProvider.GetRequiredService<CreateTransferHandler>()
                .HandleAsync(PortfolioApi.NewTransferRequest(mBankId, nestAssetId, 2_000m), cancellationToken);
            Assert.True(created.IsSuccess, created.IsFailure ? created.Error.Code : null);
            transferId = created.Value.TransferId;
        }

        var positionEventsBefore = await CountPositionEventsAsync(cancellationToken);
        SaveChanges.Reset();

        await using (var act = Provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<DeleteTransferHandler>()
                .HandleAsync(transferId, cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, SaveChanges.Count);

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var events = (await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(positionEventsBefore).ToList();
        Assert.Equal(2, events.Count);
        Assert.Equal(5_000m, Assert.Single(events, e => e.AssetId == mBankId).Quantity);
        Assert.Equal(0m, Assert.Single(events, e => e.AssetId == nestAssetId).Quantity);
        Assert.False(await verifyDb.Transactions.AnyAsync(t => t.TransferId != null, cancellationToken));
    }

    [Fact]
    public async Task MetalBuyWithCash_PublishesBoth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (cashId, metalAssetId) = await ArrangeCashAndMetalAsync(cancellationToken);
        var positionEventsBefore = await CountPositionEventsAsync(cancellationToken);
        SaveChanges.Reset();

        await using (var act = Provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<RecordTransactionHandler>()
                .HandleAsync(metalAssetId, PortfolioApi.NewMetalCashRequest(cashId), cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, SaveChanges.Count);

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var events = (await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(positionEventsBefore).ToList();
        Assert.Equal(2, events.Count);
        Assert.Equal(2m, Assert.Single(events, e => e.AssetId == metalAssetId).Quantity);
        Assert.Equal(2_600m, Assert.Single(events, e => e.AssetId == cashId).Quantity);
        var legs = await verifyDb.Transactions.Where(t => t.TransferId != null).ToListAsync(cancellationToken);
        Assert.Equal(2, legs.Count);
        Assert.Single(legs.Select(t => t.TransferId).Distinct());
    }

    [Fact]
    public async Task SecurityTrade_PublishesBothAssets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Guid cashId, brokerageId, stockId;
        await using (var arrange = Provider.CreateAsyncScope())
        {
            brokerageId = await CreateAccountAsync(arrange.ServiceProvider, "Brokerage", cancellationToken);
            await FundAccountCashAsync(arrange.ServiceProvider, brokerageId, cancellationToken, amount: 5_000m);
            cashId = (await LoadAccountCashAsync(arrange.ServiceProvider, brokerageId, cancellationToken)).Id;
            stockId = await AddStockAsync(arrange.ServiceProvider, brokerageId, "Shares", cancellationToken);
        }

        var positionEventsBefore = await CountPositionEventsAsync(cancellationToken);
        SaveChanges.Reset();

        await using (var act = Provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<RecordTransactionHandler>()
                .HandleAsync(
                    stockId,
                    new RecordTransactionRequest { Type = TransactionType.Buy, Quantity = 15m, UnitPrice = 110m, Date = AccountsApi.CashDay1 },
                    cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, SaveChanges.Count);

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var events = (await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(positionEventsBefore).ToList();
        Assert.Equal(2, events.Count);
        var stockEvent = Assert.Single(events, e => e.AssetId == stockId);
        Assert.Equal(15m, stockEvent.Quantity);
        Assert.Equal(brokerageId, stockEvent.AccountId);
        var cashEvent = Assert.Single(events, e => e.AssetId == cashId);
        Assert.Equal(3_350m, cashEvent.Quantity);
        Assert.Equal(brokerageId, cashEvent.AccountId);
        var legs = await verifyDb.Transactions.Where(t => t.TransferId != null).ToListAsync(cancellationToken);
        Assert.Equal(2, legs.Count);
        Assert.Single(legs.Select(t => t.TransferId).Distinct());
        Assert.Equal(1_650.00m, Assert.Single(legs, l => l.AssetId == cashId).Quantity);
    }

    [Fact]
    public async Task DeleteMetalTransfer_PublishesBoth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (cashId, metalAssetId) = await ArrangeCashAndMetalAsync(cancellationToken);
        await using (var record = Provider.CreateAsyncScope())
        {
            var recorded = await record.ServiceProvider.GetRequiredService<RecordTransactionHandler>()
                .HandleAsync(metalAssetId, PortfolioApi.NewMetalCashRequest(cashId), cancellationToken);
            Assert.True(recorded.IsSuccess, recorded.IsFailure ? recorded.Error.Code : null);
        }

        Guid transferId;
        await using (var read = Provider.CreateAsyncScope())
        {
            transferId = await read.ServiceProvider.GetRequiredService<PortfolioDbContext>().Transactions
                .Where(t => t.TransferId != null).Select(t => t.TransferId!.Value).FirstAsync(cancellationToken);
        }

        var positionEventsBefore = await CountPositionEventsAsync(cancellationToken);
        SaveChanges.Reset();

        await using (var act = Provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<DeleteTransferHandler>()
                .HandleAsync(transferId, cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, SaveChanges.Count);

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var events = (await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(positionEventsBefore).ToList();
        Assert.Equal(2, events.Count);
        Assert.Equal(0m, Assert.Single(events, e => e.AssetId == metalAssetId).Quantity);
        Assert.Equal(5_000m, Assert.Single(events, e => e.AssetId == cashId).Quantity);
        Assert.False(await verifyDb.Transactions.AnyAsync(t => t.TransferId != null, cancellationToken));
    }

    private async Task<(Guid CashId, Guid MetalAssetId)> ArrangeCashAndMetalAsync(
        CancellationToken cancellationToken)
    {
        await using var arrange = Provider.CreateAsyncScope();
        var cashId = await AddCashWithBalanceAsync(arrange.ServiceProvider, 5_000m, cancellationToken);
        var metal = await arrange.ServiceProvider.GetRequiredService<AddMetalHandler>()
            .HandleAsync(PortfolioApi.NewMetalRequest(metal: Metal.Gold), cancellationToken);
        Assert.True(metal.IsSuccess, metal.IsFailure ? metal.Error.Code : null);

        return (cashId, metal.Value.AssetId);
    }

    private async Task<(Guid MBankId, Guid NestAssetId)> ArrangeTwoBankAccountsAsync(
        CancellationToken cancellationToken)
    {
        await using var arrange = Provider.CreateAsyncScope();
        var mBank = await CreateBankAccountAsync(arrange.ServiceProvider, "mBank", "PLN", 0m, cancellationToken, balance: 5_000m);
        var nest = await CreateBankAccountAsync(arrange.ServiceProvider, "Nest Oszczędnościowe", "PLN", 5m, cancellationToken);

        return (mBank.CashAssetId, nest.CashAssetId);
    }
}
