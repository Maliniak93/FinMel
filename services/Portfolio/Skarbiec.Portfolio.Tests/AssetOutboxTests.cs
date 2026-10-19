using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.Accounts.ArchiveAccount;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.ArchiveAsset;
using Skarbiec.Portfolio.Features.DeleteTransaction;
using Skarbiec.Portfolio.Features.RecordTransaction;
using Skarbiec.Portfolio.Features.RemoveAsset;
using Skarbiec.Portfolio.Features.RestoreAsset;
using Skarbiec.Portfolio.Features.UpdateAsset;
using Skarbiec.Portfolio.Features.UpdateTransaction;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

public sealed class AssetOutboxTests(SkarbiecContainersFixture containers) : PortfolioOutboxTestBase(containers)
{
    private static async Task<(Guid AccountId, Guid AssetId)> AddHeldStockAsync(
        IServiceProvider services, CancellationToken cancellationToken)
    {
        var accountId = await CreateAccountAsync(services, "Outbox test account", cancellationToken);

        return (accountId, await AddAssetWithBuyAsync(services, accountId, "Shares", cancellationToken));
    }

    [Fact]
    public async Task AccountIdOnEvent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var accountId = await CreateAccountAsync(scope.ServiceProvider, "XTB IKE", cancellationToken, wrapper: TaxWrapper.Ike);
        var stockId = await AddAssetWithBuyAsync(scope.ServiceProvider, accountId, "Shares", cancellationToken);
        var cashId = await AddCashWithBalanceAsync(scope.ServiceProvider, 100m, cancellationToken);

        var events = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);

        Assert.All(events.Where(e => e.AssetId == stockId), e => Assert.Equal(accountId, e.AccountId));
        Assert.Contains(events, e => e.AssetId == stockId);
        Assert.All(events.Where(e => e.AssetId == cashId), e => Assert.Null(e.AccountId));
        Assert.Contains(events, e => e.AssetId == cashId);

        var removed = await scope.ServiceProvider.GetRequiredService<RemoveAssetHandler>().HandleAsync(stockId, cancellationToken);
        Assert.True(removed.IsSuccess);
        var removedEvent = Assert.Single(await dbContext.ReadPublishedAsync<AssetRemoved>(cancellationToken));
        Assert.Equal(accountId, removedEvent.AccountId);
    }

    [Fact]
    public async Task AddAssetWithGroup_FirstPositionCarriesGroup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var accountId = await CreateAccountAsync(scope.ServiceProvider, "Outbox test account", cancellationToken);
        var groupId = await CreateGroupAsync(scope.ServiceProvider, "Poduszka", cancellationToken);

        var added = await scope.ServiceProvider.GetRequiredService<AddAssetHandler>().HandleAsync(
            new AddAssetRequest
            {
                AccountId = accountId,
                AssetClass = AssetClass.Stock,
                Name = "Grouped",
                Currency = "PLN",
                InstrumentId = FakeInstrumentLookupClient.IdFor(AssetClass.Stock, "PLN"),
                GroupId = groupId
            },
            cancellationToken);

        Assert.True(added.IsSuccess);
        var first = (await scope.ServiceProvider.GetRequiredService<PortfolioDbContext>()
            .ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).First(e => e.AssetId == added.Value.Id);
        Assert.Equal(groupId, first.GroupId);
    }

    [Fact]
    public async Task AddAsset_WritesAssetPositionChangedInSameTransactionAsAssetRow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var accountId = await CreateAccountAsync(scope.ServiceProvider, "Outbox test account", cancellationToken);

        var handler = scope.ServiceProvider.GetRequiredService<AddAssetHandler>();
        var request = new AddAssetRequest
        {
            AccountId = accountId,
            AssetClass = AssetClass.Stock,
            Name = "Outbox test asset",
            Currency = "PLN",
            InstrumentId = FakeInstrumentLookupClient.IdFor(AssetClass.Stock, "PLN")
        };

        var result = await handler.HandleAsync(request, cancellationToken);
        Assert.True(result.IsSuccess);

        var asset = await dbContext.Assets.SingleOrDefaultAsync(a => a.Id == result.Value.Id, cancellationToken);
        Assert.NotNull(asset);

        var events = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        var evt = Assert.Single(events, e => e.AssetClass == AssetClass.Stock);
        Assert.Equal(asset.Id, evt.AssetId);
        Assert.Equal(accountId, evt.AccountId);
        Assert.Equal(UserId, evt.UserId);
        Assert.Equal("PLN", evt.Currency);
        Assert.Equal(AssetValuationMode.Market, evt.ValuationMode);
    }

    [Fact]
    public async Task UpdateAsset_WritesAssetPositionChangedWithNewInstrument()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();

        var accountId = await CreateAccountAsync(scope.ServiceProvider, "Outbox test account", cancellationToken);
        var assetResult = await scope.ServiceProvider.GetRequiredService<AddAssetHandler>().HandleAsync(
            new AddAssetRequest { AccountId = accountId, AssetClass = AssetClass.Stock, Name = "Shares", Currency = "PLN", InstrumentId = FakeInstrumentLookupClient.IdFor(AssetClass.Stock, "PLN") },
            cancellationToken);
        Assert.True(assetResult.IsSuccess);

        var instrumentId = Guid.NewGuid();
        var updateResult = await scope.ServiceProvider.GetRequiredService<UpdateAssetHandler>().HandleAsync(
            assetResult.Value.Id,
            new UpdateAssetRequest { AssetClass = AssetClass.Stock, Name = "Now another instrument", Currency = "USD", InstrumentId = instrumentId },
            cancellationToken);
        Assert.True(updateResult.IsSuccess);

        var events = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        var updateEvent = Assert.Single(events, e => e.Version > 0);
        Assert.Equal(AssetValuationMode.Market, updateEvent.ValuationMode);
        Assert.Equal(accountId, updateEvent.AccountId);
        Assert.Equal(instrumentId, updateEvent.InstrumentId);
        Assert.Equal("USD", updateEvent.Currency);
    }

    [Fact]
    public async Task RemoveAsset_WritesAssetRemovedInSameTransactionAsDeletion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();

        var accountId = await CreateAccountAsync(scope.ServiceProvider, "Outbox test account", cancellationToken);
        var assetId = await AddStockAsync(scope.ServiceProvider, accountId, "Shares", cancellationToken);

        var removeResult = await scope.ServiceProvider.GetRequiredService<RemoveAssetHandler>()
            .HandleAsync(assetId, cancellationToken);
        Assert.True(removeResult.IsSuccess);

        var remainingAsset = await dbContext.Assets.SingleOrDefaultAsync(a => a.Id == assetId, cancellationToken);
        Assert.Null(remainingAsset);

        var events = await dbContext.ReadPublishedAsync<AssetRemoved>(cancellationToken);
        var evt = Assert.Single(events);
        Assert.Equal(assetId, evt.AssetId);
        Assert.Equal(accountId, evt.AccountId);
        Assert.Equal(UserId, evt.UserId);
    }

    [Fact]
    public async Task SuccessiveMutations_PublishStrictlyIncreasingVersionPerAsset()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();

        var accountId = await CreateAccountAsync(scope.ServiceProvider, "Outbox test account", cancellationToken);
        var assetId = await AddStockAsync(scope.ServiceProvider, accountId, "Shares", cancellationToken);

        var updated = await scope.ServiceProvider.GetRequiredService<UpdateAssetHandler>().HandleAsync(
            assetId,
            new UpdateAssetRequest
            {
                AssetClass = AssetClass.Stock,
                Name = "Shares renamed",
                Currency = "PLN",
                InstrumentId = FakeInstrumentLookupClient.IdFor(AssetClass.Stock, "PLN")
            },
            cancellationToken);
        Assert.True(updated.IsSuccess);

        var events = (await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken))
            .Where(e => e.AssetId == assetId)
            .ToList();
        Assert.Equal(2, events.Count);
        Assert.True(events[1].Version > events[0].Version);
    }

    [Fact]
    public async Task RemoveAsset_WithTransactions_RemovesTransactionsAndWritesAssetRemoved()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();

        var accountId = await CreateAccountAsync(scope.ServiceProvider, "Outbox test account", cancellationToken);
        var assetId = await AddAssetWithBuyAsync(scope.ServiceProvider, accountId, "Shares", cancellationToken);
        var keptAssetId = await AddAssetWithBuyAsync(scope.ServiceProvider, accountId, "Kept shares", cancellationToken);

        SaveChanges.Reset();

        var removeResult = await scope.ServiceProvider.GetRequiredService<RemoveAssetHandler>()
            .HandleAsync(assetId, cancellationToken);

        Assert.True(removeResult.IsSuccess);
        Assert.Equal(1, SaveChanges.Count);

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        Assert.False(await verifyDb.Assets.AnyAsync(a => a.Id == assetId, cancellationToken));
        Assert.False(await verifyDb.Transactions.AnyAsync(t => t.AssetId == assetId, cancellationToken));
        Assert.True(await verifyDb.Transactions.AnyAsync(t => t.AssetId == keptAssetId, cancellationToken));
        Assert.True(await verifyDb.Accounts.AnyAsync(a => a.Id == accountId, cancellationToken));

        var evt = Assert.Single(await verifyDb.ReadPublishedAsync<AssetRemoved>(cancellationToken));
        Assert.Equal(assetId, evt.AssetId);
        Assert.Equal(accountId, evt.AccountId);
        Assert.Equal(UserId, evt.UserId);
    }

    [Fact]
    public async Task DisallowedTransactionTypeOnCashLikeAsset_WritesNoEvent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid accountId;
        Guid cashAssetId;
        Guid depositId;
        Guid stockAssetId;
        await using (var arrange = Provider.CreateAsyncScope())
        {
            accountId = await CreateAccountAsync(arrange.ServiceProvider, "Cash rule outbox account", cancellationToken);
            cashAssetId = await AddCashWithBalanceAsync(arrange.ServiceProvider, 1_000m, cancellationToken);
            stockAssetId = await AddAssetWithBuyAsync(arrange.ServiceProvider, accountId, "Shares", cancellationToken);
        }

        int outboxRowsBefore;
        await using (var before = Provider.CreateAsyncScope())
        {
            var db = before.ServiceProvider.GetRequiredService<PortfolioDbContext>();
            depositId = await db.Transactions.Where(t => t.AssetId == cashAssetId).Select(t => t.Id).SingleAsync(cancellationToken);
            outboxRowsBefore = await db.Set<OutboxMessage>().CountAsync(cancellationToken);
        }

        var writes = new (string Name, Func<IServiceProvider, Task<Error>> Write)[]
        {
            ("RecordTransaction", async s => (await s.GetRequiredService<RecordTransactionHandler>().HandleAsync(
                cashAssetId,
                new RecordTransactionRequest { Type = TransactionType.Buy, Quantity = 1m, UnitPrice = 10m, Date = new DateOnly(2026, 1, 3) },
                cancellationToken)).Error),
            ("UpdateTransaction", async s => (await s.GetRequiredService<UpdateTransactionHandler>().HandleAsync(
                cashAssetId,
                depositId,
                new UpdateTransactionRequest { Type = TransactionType.Sell, Quantity = 100m, UnitPrice = 1m, Date = new DateOnly(2026, 1, 1) },
                cancellationToken)).Error),
            ("UpdateAsset", async s => (await s.GetRequiredService<UpdateAssetHandler>().HandleAsync(
                stockAssetId,
                new UpdateAssetRequest { AssetClass = AssetClass.Cash, Name = "Now cash", Currency = "PLN" },
                cancellationToken)).Error),
        };

        foreach (var (name, write) in writes)
        {
            await using var scope = Provider.CreateAsyncScope();
            var error = await write(scope.ServiceProvider);
            Assert.True(
                error.Code == PortfolioAssertions.TransactionTypeNotAllowedErrorCode,
                $"{name} with a type the cash-like class does not accept returned '{error.Code}', expected '{PortfolioAssertions.TransactionTypeNotAllowedErrorCode}'.");
        }

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        Assert.Equal(outboxRowsBefore, await verifyDb.Set<OutboxMessage>().CountAsync(cancellationToken));
        Assert.Equal(2, await verifyDb.Assets.ExcludingAccountCash().CountAsync(cancellationToken));
        var cash = await verifyDb.Assets.SingleAsync(a => a.Id == cashAssetId, cancellationToken);
        Assert.Equal(1_000m, cash.Quantity);
        var deposit = await verifyDb.Transactions.SingleAsync(t => t.AssetId == cashAssetId, cancellationToken);
        Assert.Equal(depositId, deposit.Id);
        Assert.Equal(TransactionType.Deposit, deposit.Type);
        var stock = await verifyDb.Assets.SingleAsync(a => a.Id == stockAssetId, cancellationToken);
        Assert.Equal(AssetClass.Stock, stock.AssetClass);
    }

    [Fact]
    public async Task ArchiveAsset_PublishesPositionChangedWithArchivedFlag()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var (accountId, assetId) = await AddHeldStockAsync(scope.ServiceProvider, cancellationToken);
        var before = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        var versionBefore = before.Where(e => e.AssetId == assetId).Max(e => e.Version);
        SaveChanges.Reset();

        var result = await scope.ServiceProvider.GetRequiredService<ArchiveAssetHandler>()
            .HandleAsync(assetId, cancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(1, SaveChanges.Count);
        var after = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        Assert.Equal(before.Count + 1, after.Count);
        var evt = after[^1];
        Assert.Equal(assetId, evt.AssetId);
        Assert.Equal(accountId, evt.AccountId);
        Assert.Equal(UserId, evt.UserId);
        Assert.True(evt.IsArchived);
        Assert.Equal(5m, evt.Quantity);
        Assert.True(evt.Version > versionBefore);
    }

    [Fact]
    public async Task ArchiveAsset_AlreadyArchived_WritesNoEvent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var (_, assetId) = await AddHeldStockAsync(scope.ServiceProvider, cancellationToken);
        var handler = scope.ServiceProvider.GetRequiredService<ArchiveAssetHandler>();
        Assert.True((await handler.HandleAsync(assetId, cancellationToken)).IsSuccess);
        var outboxRowsBefore = await dbContext.Set<OutboxMessage>().CountAsync(cancellationToken);

        var second = await handler.HandleAsync(assetId, cancellationToken);

        Assert.True(second.IsSuccess);
        Assert.True(second.Value.IsArchived);
        Assert.Equal(outboxRowsBefore, await dbContext.Set<OutboxMessage>().CountAsync(cancellationToken));
    }

    [Fact]
    public async Task RestoreAsset_PublishesPositionChangedWithoutArchivedFlag()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var (_, assetId) = await AddHeldStockAsync(scope.ServiceProvider, cancellationToken);
        Assert.True((await scope.ServiceProvider.GetRequiredService<ArchiveAssetHandler>()
            .HandleAsync(assetId, cancellationToken)).IsSuccess);
        var before = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        var versionBefore = before.Where(e => e.AssetId == assetId).Max(e => e.Version);
        SaveChanges.Reset();

        var result = await scope.ServiceProvider.GetRequiredService<RestoreAssetHandler>()
            .HandleAsync(assetId, cancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(1, SaveChanges.Count);
        var after = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        Assert.Equal(before.Count + 1, after.Count);
        var evt = after[^1];
        Assert.Equal(assetId, evt.AssetId);
        Assert.False(evt.IsArchived);
        Assert.True(evt.Version > versionBefore);
    }

    [Fact]
    public async Task RestoreAsset_NotArchived_WritesNoEvent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var (_, assetId) = await AddHeldStockAsync(scope.ServiceProvider, cancellationToken);
        var outboxRowsBefore = await dbContext.Set<OutboxMessage>().CountAsync(cancellationToken);

        var result = await scope.ServiceProvider.GetRequiredService<RestoreAssetHandler>()
            .HandleAsync(assetId, cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsArchived);
        Assert.Equal(outboxRowsBefore, await dbContext.Set<OutboxMessage>().CountAsync(cancellationToken));
    }

    [Fact]
    public async Task ArchiveAndRestoreAsset_ArchivedAccountOrUnknownAsset_WriteNoEvent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid accountId;
        Guid assetId;
        await using (var arrange = Provider.CreateAsyncScope())
        {
            accountId = await CreateAccountAsync(arrange.ServiceProvider, "Archived outbox account", cancellationToken);
            assetId = await AddStockAsync(arrange.ServiceProvider, accountId, "Shares", cancellationToken);
            Assert.True((await arrange.ServiceProvider.GetRequiredService<ArchiveAccountHandler>()
                .HandleAsync(accountId, cancellationToken)).IsSuccess);
        }

        int outboxRowsBefore;
        await using (var before = Provider.CreateAsyncScope())
        {
            outboxRowsBefore = await before.ServiceProvider.GetRequiredService<PortfolioDbContext>()
                .Set<OutboxMessage>().CountAsync(cancellationToken);
        }

        var calls = new (string Name, Func<IServiceProvider, Task<Error>> Call, string ExpectedCode)[]
        {
            ("Archive", async s => (await s.GetRequiredService<ArchiveAssetHandler>().HandleAsync(assetId, cancellationToken)).Error, PortfolioAssertions.AccountArchivedErrorCode),
            ("Restore", async s => (await s.GetRequiredService<RestoreAssetHandler>().HandleAsync(assetId, cancellationToken)).Error, PortfolioAssertions.AccountArchivedErrorCode),
            ("Archive unknown", async s => (await s.GetRequiredService<ArchiveAssetHandler>().HandleAsync(Guid.NewGuid(), cancellationToken)).Error, "NotFound"),
        };

        foreach (var (name, call, expectedCode) in calls)
        {
            await using var scope = Provider.CreateAsyncScope();
            var error = await call(scope.ServiceProvider);
            Assert.True(
                error.Code.StartsWith(expectedCode, StringComparison.Ordinal),
                $"{name} returned '{error.Code}', expected a code starting '{expectedCode}'.");
        }

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        Assert.Equal(outboxRowsBefore, await verifyDb.Set<OutboxMessage>().CountAsync(cancellationToken));
        Assert.False((await verifyDb.Assets.SingleAsync(a => a.Id == assetId, cancellationToken)).IsArchived);
    }

    [Fact]
    public async Task WriteToArchivedAsset_WritesNoEvent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid assetId;
        await using (var arrange = Provider.CreateAsyncScope())
        {
            (_, assetId) = await AddHeldStockAsync(arrange.ServiceProvider, cancellationToken);
            Assert.True((await arrange.ServiceProvider.GetRequiredService<ArchiveAssetHandler>()
                .HandleAsync(assetId, cancellationToken)).IsSuccess);
        }

        Guid transactionId;
        int outboxRowsBefore;
        await using (var before = Provider.CreateAsyncScope())
        {
            var db = before.ServiceProvider.GetRequiredService<PortfolioDbContext>();
            transactionId = await db.Transactions.Where(t => t.AssetId == assetId).Select(t => t.Id).SingleAsync(cancellationToken);
            outboxRowsBefore = await db.Set<OutboxMessage>().CountAsync(cancellationToken);
        }

        var writes = new (string Name, Func<IServiceProvider, Task<Error>> Write)[]
        {
            ("UpdateAsset", async s => (await s.GetRequiredService<UpdateAssetHandler>().HandleAsync(
                assetId,
                new UpdateAssetRequest
                {
                    AssetClass = AssetClass.Stock,
                    Name = "Renamed",
                    Currency = "PLN",
                    InstrumentId = FakeInstrumentLookupClient.IdFor(AssetClass.Stock, "PLN")
                },
                cancellationToken)).Error),
            ("RecordTransaction", async s => (await s.GetRequiredService<RecordTransactionHandler>().HandleAsync(
                assetId,
                new RecordTransactionRequest { Type = TransactionType.Buy, Quantity = 1m, UnitPrice = 10m, Date = new DateOnly(2026, 1, 3) },
                cancellationToken)).Error),
            ("UpdateTransaction", async s => (await s.GetRequiredService<UpdateTransactionHandler>().HandleAsync(
                assetId,
                transactionId,
                new UpdateTransactionRequest { Type = TransactionType.Buy, Quantity = 50m, UnitPrice = 10m, Date = new DateOnly(2026, 1, 2) },
                cancellationToken)).Error),
            ("DeleteTransaction", async s => (await s.GetRequiredService<DeleteTransactionHandler>()
                .HandleAsync(assetId, transactionId, cancellationToken)).Error),
        };

        foreach (var (name, write) in writes)
        {
            await using var scope = Provider.CreateAsyncScope();
            var error = await write(scope.ServiceProvider);
            Assert.True(
                error.Code == PortfolioAssertions.AssetArchivedErrorCode,
                $"{name} on an archived asset returned '{error.Code}', expected '{PortfolioAssertions.AssetArchivedErrorCode}'.");
        }

        await using (var verify = Provider.CreateAsyncScope())
        {
            var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
            Assert.Equal(outboxRowsBefore, await verifyDb.Set<OutboxMessage>().CountAsync(cancellationToken));
            var asset = await verifyDb.Assets.SingleAsync(a => a.Id == assetId, cancellationToken);
            Assert.Equal("Shares", asset.Name);
            Assert.Equal(5m, asset.Quantity);
            Assert.Equal(5m, (await verifyDb.Transactions.SingleAsync(t => t.Id == transactionId, cancellationToken)).Quantity);
        }

        await using var remove = Provider.CreateAsyncScope();
        var removeResult = await remove.ServiceProvider.GetRequiredService<RemoveAssetHandler>()
            .HandleAsync(assetId, cancellationToken);

        Assert.True(removeResult.IsSuccess, removeResult.IsFailure ? removeResult.Error.Code : null);
        var removeDb = remove.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        Assert.False(await removeDb.Assets.AnyAsync(a => a.Id == assetId, cancellationToken));
        Assert.Equal(assetId, Assert.Single(await removeDb.ReadPublishedAsync<AssetRemoved>(cancellationToken)).AssetId);
    }

    [Fact]
    public async Task Add_NoTransactions_FirstTransactionDateNull()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();

        var accountId = await CreateAccountAsync(scope.ServiceProvider, "Outbox test account", cancellationToken);
        var result = await scope.ServiceProvider.GetRequiredService<AddAssetHandler>().HandleAsync(
            new AddAssetRequest { AccountId = accountId, AssetClass = AssetClass.Stock, Name = "Shares", Currency = "PLN", InstrumentId = FakeInstrumentLookupClient.IdFor(AssetClass.Stock, "PLN") },
            cancellationToken);
        Assert.True(result.IsSuccess);

        var evt = Assert.Single(
            await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken), e => e.AssetClass == AssetClass.Stock);
        Assert.Null(evt.FirstTransactionDate);
    }

    [Fact]
    public async Task Add_NoTransactions_EmptyQuantityHistory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
        var accountId = await CreateAccountAsync(scope.ServiceProvider, "Outbox test account", cancellationToken);
        await AddStockAsync(scope.ServiceProvider, accountId, "Shares", cancellationToken);

        var evt = Assert.Single(
            await scope.ServiceProvider.GetRequiredService<PortfolioDbContext>().ReadPublishedAsync<AssetPositionChanged>(cancellationToken),
            e => e.AssetClass == AssetClass.Stock);
        Assert.Empty(evt.QuantityHistory);
    }
}
