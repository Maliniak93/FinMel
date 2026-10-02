using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.SavingsAccounts.AddSavingsAccount;
using Skarbiec.Portfolio.Features.Transfers.CreateTransfer;
using Skarbiec.Portfolio.Features.Transfers.DeleteTransfer;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

/// <summary>
/// Outbox tests for transfers between Cash and a savings account: create and delete.
/// The hostless provider and shared arrange helpers live in <see cref="PortfolioOutboxTestBase"/>.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class TransferOutboxTests(SkarbiecContainersFixture containers) : PortfolioOutboxTestBase(containers)
{
    /// <summary>
    /// savings-cash-transfers AC-2 (outbox half): a Cash → Savings transfer writes both legs and one
    /// <see cref="AssetPositionChanged"/> per asset - Cash at 3 000, the savings account at 2 000 - in a
    /// single save.
    /// </summary>
    [Fact]
    public async Task CreateTransfer_PublishesPositionChangedForBothAssets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var (walletId, cashId, savingsId, savingsAssetId) = await ArrangeCashAndSavingsAsync(cancellationToken);
        var positionEventsBefore = await CountPositionEventsAsync(cancellationToken);
        SaveChanges.Reset();

        await using (var act = Provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<CreateTransferHandler>()
                .HandleAsync(PortfolioApi.NewTransferRequest(cashId, savingsAssetId, 2_000m), cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, SaveChanges.Count);

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var events = (await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(positionEventsBefore).ToList();
        Assert.Equal(2, events.Count);
        var cashEvent = Assert.Single(events, e => e.AssetId == cashId);
        Assert.Equal(3_000m, cashEvent.Quantity);
        Assert.Equal(walletId, cashEvent.PortfolioId);
        Assert.Equal(UserId, cashEvent.UserId);
        var savingsEvent = Assert.Single(events, e => e.AssetId == savingsAssetId);
        Assert.Equal(2_000m, savingsEvent.Quantity);
        Assert.Equal(savingsId, savingsEvent.PortfolioId);
        Assert.Equal(AssetClass.Savings, savingsEvent.AssetClass);

        var legs = await verifyDb.Transactions.Where(t => t.TransferId != null).ToListAsync(cancellationToken);
        Assert.Equal(2, legs.Count);
        Assert.Single(legs.Select(t => t.TransferId).Distinct());
    }

    /// <summary>
    /// savings-cash-transfers AC-4 (outbox half): deleting the transfer removes both legs and writes one
    /// further <see cref="AssetPositionChanged"/> per asset - Cash back at 5 000, the account at 0 - in a
    /// single save.
    /// </summary>
    [Fact]
    public async Task DeleteTransfer_PublishesPositionChangedForBothAssets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var (_, cashId, _, savingsAssetId) = await ArrangeCashAndSavingsAsync(cancellationToken);
        Guid transferId;
        await using (var create = Provider.CreateAsyncScope())
        {
            var created = await create.ServiceProvider.GetRequiredService<CreateTransferHandler>()
                .HandleAsync(PortfolioApi.NewTransferRequest(cashId, savingsAssetId, 2_000m), cancellationToken);
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
        Assert.Equal(5_000m, Assert.Single(events, e => e.AssetId == cashId).Quantity);
        Assert.Equal(0m, Assert.Single(events, e => e.AssetId == savingsAssetId).Quantity);
        Assert.False(await verifyDb.Transactions.AnyAsync(t => t.TransferId != null, cancellationToken));
    }

    private async Task<(Guid WalletId, Guid CashId, Guid SavingsPortfolioId, Guid SavingsAssetId)> ArrangeCashAndSavingsAsync(
        CancellationToken cancellationToken)
    {
        await using var arrange = Provider.CreateAsyncScope();
        var walletId = await CreatePortfolioAsync(arrange.ServiceProvider, "Wallet", cancellationToken);
        var cashId = await AddCashWithBalanceAsync(arrange.ServiceProvider, walletId, 5_000m, cancellationToken);
        var savingsPortfolioId = await CreatePortfolioAsync(arrange.ServiceProvider, "Savings", cancellationToken);
        var account = await arrange.ServiceProvider.GetRequiredService<AddSavingsAccountHandler>()
            .HandleAsync(savingsPortfolioId, PortfolioApi.NewSavingsAccountRequest(withOpeningDeposit: false), cancellationToken);
        Assert.True(account.IsSuccess, account.IsFailure ? account.Error.Code : null);

        return (walletId, cashId, savingsPortfolioId, account.Value.AssetId);
    }
}
