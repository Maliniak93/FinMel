using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.ArchiveAsset;
using Skarbiec.Portfolio.Features.ArchivePortfolio;
using Skarbiec.Portfolio.Features.CreatePortfolio;
using Skarbiec.Portfolio.Features.DeleteTransaction;
using Skarbiec.Portfolio.Features.RecordTransaction;
using Skarbiec.Portfolio.Features.RemoveAsset;
using Skarbiec.Portfolio.Features.RestoreAsset;
using Skarbiec.Portfolio.Features.UpdateAsset;
using Skarbiec.Portfolio.Features.UpdateTransaction;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class AssetOutboxTests(SkarbiecContainersFixture containers) : PortfolioOutboxTestBase(containers)
{
    [Fact]
    public async Task AddAsset_WritesAssetPositionChangedInSameTransactionAsAssetRow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();

        var portfolioResult = await scope.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = "Outbox test portfolio" }, cancellationToken);
        Assert.True(portfolioResult.IsSuccess);

        var handler = scope.ServiceProvider.GetRequiredService<AddAssetHandler>();
        var request = new AddAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Outbox test asset",
            Currency = "PLN",
            ManualValue = 100m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var result = await handler.HandleAsync(portfolioResult.Value.Id, request, cancellationToken);
        Assert.True(result.IsSuccess);

        var asset = await dbContext.Assets.SingleOrDefaultAsync(a => a.Id == result.Value.Id, cancellationToken);
        Assert.NotNull(asset);

        var events = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        var evt = Assert.Single(events);
        Assert.Equal(asset.Id, evt.AssetId);
        Assert.Equal(portfolioResult.Value.Id, evt.PortfolioId);
        Assert.Equal(UserId, evt.UserId);
        Assert.Equal(AssetClass.Cash, evt.AssetClass);
        Assert.Equal("PLN", evt.Currency);
        Assert.Equal(100m, evt.ManualValueAmount);
        Assert.False(evt.PortfolioIsArchived);
    }

    [Fact]
    public async Task UpdateAsset_WritesAssetPositionChangedWithNewValuationMode()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();

        var portfolioResult = await scope.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = "Outbox test portfolio" }, cancellationToken);
        var assetResult = await scope.ServiceProvider.GetRequiredService<AddAssetHandler>().HandleAsync(
            portfolioResult.Value.Id,
            new AddAssetRequest { AssetClass = AssetClass.Cash, Name = "Cash", Currency = "PLN", ManualValue = 0m, ManualValueDate = new DateOnly(2026, 1, 1) },
            cancellationToken);
        Assert.True(assetResult.IsSuccess);

        var instrumentId = Guid.NewGuid();
        var updateResult = await scope.ServiceProvider.GetRequiredService<UpdateAssetHandler>().HandleAsync(
            portfolioResult.Value.Id,
            assetResult.Value.Id,
            new UpdateAssetRequest { AssetClass = AssetClass.Stock, Name = "Now market", Currency = "USD", InstrumentId = instrumentId },
            cancellationToken);
        Assert.True(updateResult.IsSuccess);

        var events = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        var updateEvent = Assert.Single(events, e => e.Version > 0);
        Assert.Equal(AssetValuationMode.Market, updateEvent.ValuationMode);
        Assert.Equal(instrumentId, updateEvent.InstrumentId);
        Assert.Equal("USD", updateEvent.Currency);
        Assert.Null(updateEvent.ManualValueAmount);
        Assert.Null(updateEvent.ManualValueDate);
    }

    [Fact]
    public async Task RemoveAsset_WritesAssetRemovedInSameTransactionAsDeletion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();

        var portfolioResult = await scope.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = "Outbox test portfolio" }, cancellationToken);
        var assetResult = await scope.ServiceProvider.GetRequiredService<AddAssetHandler>().HandleAsync(
            portfolioResult.Value.Id,
            new AddAssetRequest { AssetClass = AssetClass.Cash, Name = "Cash", Currency = "PLN", ManualValue = 0m, ManualValueDate = new DateOnly(2026, 1, 1) },
            cancellationToken);

        var removeResult = await scope.ServiceProvider.GetRequiredService<RemoveAssetHandler>()
            .HandleAsync(portfolioResult.Value.Id, assetResult.Value.Id, cancellationToken);
        Assert.True(removeResult.IsSuccess);

        var remainingAsset = await dbContext.Assets.SingleOrDefaultAsync(a => a.Id == assetResult.Value.Id, cancellationToken);
        Assert.Null(remainingAsset);

        var events = await dbContext.ReadPublishedAsync<AssetRemoved>(cancellationToken);
        var evt = Assert.Single(events);
        Assert.Equal(assetResult.Value.Id, evt.AssetId);
        Assert.Equal(portfolioResult.Value.Id, evt.PortfolioId);
        Assert.Equal(UserId, evt.UserId);
    }

    [Fact]
    public async Task SuccessiveMutations_PublishStrictlyIncreasingVersionPerAsset()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();

        var portfolioResult = await scope.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = "Outbox test portfolio" }, cancellationToken);
        var assetResult = await scope.ServiceProvider.GetRequiredService<AddAssetHandler>().HandleAsync(
            portfolioResult.Value.Id,
            new AddAssetRequest { AssetClass = AssetClass.Cash, Name = "Cash", Currency = "PLN", ManualValue = 0m, ManualValueDate = new DateOnly(2026, 1, 1) },
            cancellationToken);

        await scope.ServiceProvider.GetRequiredService<UpdateAssetHandler>().HandleAsync(
            portfolioResult.Value.Id,
            assetResult.Value.Id,
            new UpdateAssetRequest { AssetClass = AssetClass.Cash, Name = "Cash renamed", Currency = "PLN", ManualValue = 50m, ManualValueDate = new DateOnly(2026, 1, 2) },
            cancellationToken);

        var events = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        Assert.Equal(2, events.Count);
        Assert.True(events[1].Version > events[0].Version);
    }

    [Fact]
    public async Task RemoveAsset_WithTransactions_RemovesTransactionsAndWritesAssetRemoved()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();

        var portfolioId = (await scope.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = "Outbox test portfolio" }, cancellationToken)).Value.Id;
        var assetId = await AddAssetWithBuyAsync(scope.ServiceProvider, portfolioId, "Shares", cancellationToken);
        var keptAssetId = await AddAssetWithBuyAsync(scope.ServiceProvider, portfolioId, "Kept shares", cancellationToken);

        SaveChanges.Reset();

        var removeResult = await scope.ServiceProvider.GetRequiredService<RemoveAssetHandler>()
            .HandleAsync(portfolioId, assetId, cancellationToken);

        Assert.True(removeResult.IsSuccess);
        Assert.Equal(1, SaveChanges.Count);

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        Assert.False(await verifyDb.Assets.AnyAsync(a => a.Id == assetId, cancellationToken));
        Assert.False(await verifyDb.Transactions.AnyAsync(t => t.AssetId == assetId, cancellationToken));
        Assert.True(await verifyDb.Transactions.AnyAsync(t => t.AssetId == keptAssetId, cancellationToken));
        Assert.True(await verifyDb.Portfolios.AnyAsync(p => p.Id == portfolioId, cancellationToken));

        var evt = Assert.Single(await verifyDb.ReadPublishedAsync<AssetRemoved>(cancellationToken));
        Assert.Equal(assetId, evt.AssetId);
        Assert.Equal(portfolioId, evt.PortfolioId);
        Assert.Equal(UserId, evt.UserId);
        Assert.False(evt.CascadedFromPortfolio);
    }

    [Fact]
    public async Task DisallowedTransactionTypeOnCashLikeAsset_WritesNoEvent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid portfolioId;
        Guid cashAssetId;
        Guid depositId;
        Guid stockAssetId;
        await using (var arrange = Provider.CreateAsyncScope())
        {
            portfolioId = (await arrange.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
                .HandleAsync(new CreatePortfolioRequest { Name = "Cash rule outbox portfolio" }, cancellationToken)).Value.Id;
            var cashResult = await arrange.ServiceProvider.GetRequiredService<AddAssetHandler>().HandleAsync(
                portfolioId,
                new AddAssetRequest
                {
                    AssetClass = AssetClass.Cash,
                    Name = "Wallet",
                    Currency = "PLN",
                    InitialTransaction = new RecordTransactionRequest
                    {
                        Type = TransactionType.Deposit,
                        Quantity = 1_000m,
                        UnitPrice = 1m,
                        Date = new DateOnly(2026, 1, 1)
                    }
                },
                cancellationToken);
            Assert.True(cashResult.IsSuccess);
            cashAssetId = cashResult.Value.Id;
            stockAssetId = await AddAssetWithBuyAsync(arrange.ServiceProvider, portfolioId, "Shares", cancellationToken);
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
                portfolioId,
                cashAssetId,
                new RecordTransactionRequest { Type = TransactionType.Buy, Quantity = 1m, UnitPrice = 10m, Date = new DateOnly(2026, 1, 3) },
                cancellationToken)).Error),
            ("AddAsset", async s => (await s.GetRequiredService<AddAssetHandler>().HandleAsync(
                portfolioId,
                new AddAssetRequest
                {
                    AssetClass = AssetClass.Cash,
                    Name = "Second wallet",
                    Currency = "PLN",
                    InitialTransaction = new RecordTransactionRequest
                    {
                        Type = TransactionType.Interest, Quantity = 10m, UnitPrice = 1m, Date = new DateOnly(2026, 1, 3)
                    }
                },
                cancellationToken)).Error),
            ("UpdateTransaction", async s => (await s.GetRequiredService<UpdateTransactionHandler>().HandleAsync(
                portfolioId,
                cashAssetId,
                depositId,
                new UpdateTransactionRequest { Type = TransactionType.Sell, Quantity = 100m, UnitPrice = 1m, Date = new DateOnly(2026, 1, 1) },
                cancellationToken)).Error),
            ("UpdateAsset", async s => (await s.GetRequiredService<UpdateAssetHandler>().HandleAsync(
                portfolioId,
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
        Assert.Equal(2, await verifyDb.Assets.CountAsync(a => a.PortfolioId == portfolioId, cancellationToken));
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
        var portfolioId = await CreatePortfolioAsync(scope.ServiceProvider, "Outbox test portfolio", cancellationToken);
        var assetId = await AddCashWithBalanceAsync(scope.ServiceProvider, portfolioId, 100m, cancellationToken);
        var before = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        var versionBefore = before.Where(e => e.AssetId == assetId).Max(e => e.Version);
        SaveChanges.Reset();

        var result = await scope.ServiceProvider.GetRequiredService<ArchiveAssetHandler>()
            .HandleAsync(portfolioId, assetId, cancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(1, SaveChanges.Count);
        var after = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        Assert.Equal(before.Count + 1, after.Count);
        var evt = after[^1];
        Assert.Equal(assetId, evt.AssetId);
        Assert.Equal(portfolioId, evt.PortfolioId);
        Assert.Equal(UserId, evt.UserId);
        Assert.True(evt.IsArchived);
        Assert.False(evt.PortfolioIsArchived);
        Assert.Equal(100m, evt.Quantity);
        Assert.True(evt.Version > versionBefore);
    }

    [Fact]
    public async Task ArchiveAsset_AlreadyArchived_WritesNoEvent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var portfolioId = await CreatePortfolioAsync(scope.ServiceProvider, "Outbox test portfolio", cancellationToken);
        var assetId = await AddCashWithBalanceAsync(scope.ServiceProvider, portfolioId, 100m, cancellationToken);
        var handler = scope.ServiceProvider.GetRequiredService<ArchiveAssetHandler>();
        Assert.True((await handler.HandleAsync(portfolioId, assetId, cancellationToken)).IsSuccess);
        var outboxRowsBefore = await dbContext.Set<OutboxMessage>().CountAsync(cancellationToken);

        var second = await handler.HandleAsync(portfolioId, assetId, cancellationToken);

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
        var portfolioId = await CreatePortfolioAsync(scope.ServiceProvider, "Outbox test portfolio", cancellationToken);
        var assetId = await AddCashWithBalanceAsync(scope.ServiceProvider, portfolioId, 100m, cancellationToken);
        Assert.True((await scope.ServiceProvider.GetRequiredService<ArchiveAssetHandler>()
            .HandleAsync(portfolioId, assetId, cancellationToken)).IsSuccess);
        var before = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        var versionBefore = before.Where(e => e.AssetId == assetId).Max(e => e.Version);
        SaveChanges.Reset();

        var result = await scope.ServiceProvider.GetRequiredService<RestoreAssetHandler>()
            .HandleAsync(portfolioId, assetId, cancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(1, SaveChanges.Count);
        var after = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        Assert.Equal(before.Count + 1, after.Count);
        var evt = after[^1];
        Assert.Equal(assetId, evt.AssetId);
        Assert.False(evt.IsArchived);
        Assert.False(evt.PortfolioIsArchived);
        Assert.True(evt.Version > versionBefore);
    }

    [Fact]
    public async Task RestoreAsset_NotArchived_WritesNoEvent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var portfolioId = await CreatePortfolioAsync(scope.ServiceProvider, "Outbox test portfolio", cancellationToken);
        var assetId = await AddCashWithBalanceAsync(scope.ServiceProvider, portfolioId, 100m, cancellationToken);
        var outboxRowsBefore = await dbContext.Set<OutboxMessage>().CountAsync(cancellationToken);

        var result = await scope.ServiceProvider.GetRequiredService<RestoreAssetHandler>()
            .HandleAsync(portfolioId, assetId, cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsArchived);
        Assert.Equal(outboxRowsBefore, await dbContext.Set<OutboxMessage>().CountAsync(cancellationToken));
    }

    [Fact]
    public async Task ArchiveAndRestoreAsset_ArchivedPortfolioOrUnknownAsset_WriteNoEvent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid portfolioId;
        Guid assetId;
        await using (var arrange = Provider.CreateAsyncScope())
        {
            portfolioId = await CreatePortfolioAsync(arrange.ServiceProvider, "Archived outbox portfolio", cancellationToken);
            assetId = await AddCashWithBalanceAsync(arrange.ServiceProvider, portfolioId, 100m, cancellationToken);
            Assert.True((await arrange.ServiceProvider.GetRequiredService<ArchivePortfolioHandler>()
                .HandleAsync(portfolioId, cancellationToken)).IsSuccess);
        }

        int outboxRowsBefore;
        await using (var before = Provider.CreateAsyncScope())
        {
            outboxRowsBefore = await before.ServiceProvider.GetRequiredService<PortfolioDbContext>()
                .Set<OutboxMessage>().CountAsync(cancellationToken);
        }

        var calls = new (string Name, Func<IServiceProvider, Task<Error>> Call, string ExpectedCode)[]
        {
            ("Archive", async s => (await s.GetRequiredService<ArchiveAssetHandler>().HandleAsync(portfolioId, assetId, cancellationToken)).Error, PortfolioAssertions.PortfolioArchivedErrorCode),
            ("Restore", async s => (await s.GetRequiredService<RestoreAssetHandler>().HandleAsync(portfolioId, assetId, cancellationToken)).Error, PortfolioAssertions.PortfolioArchivedErrorCode),
            ("Archive unknown", async s => (await s.GetRequiredService<ArchiveAssetHandler>().HandleAsync(portfolioId, Guid.NewGuid(), cancellationToken)).Error, "NotFound"),
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

        Guid portfolioId;
        Guid assetId;
        await using (var arrange = Provider.CreateAsyncScope())
        {
            portfolioId = await CreatePortfolioAsync(arrange.ServiceProvider, "Archived asset portfolio", cancellationToken);
            assetId = await AddCashWithBalanceAsync(arrange.ServiceProvider, portfolioId, 100m, cancellationToken);
            Assert.True((await arrange.ServiceProvider.GetRequiredService<ArchiveAssetHandler>()
                .HandleAsync(portfolioId, assetId, cancellationToken)).IsSuccess);
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
                portfolioId,
                assetId,
                new UpdateAssetRequest { AssetClass = AssetClass.Cash, Name = "Renamed", Currency = "PLN" },
                cancellationToken)).Error),
            ("RecordTransaction", async s => (await s.GetRequiredService<RecordTransactionHandler>().HandleAsync(
                portfolioId,
                assetId,
                new RecordTransactionRequest { Type = TransactionType.Deposit, Quantity = 1m, UnitPrice = 1m, Date = new DateOnly(2026, 1, 3) },
                cancellationToken)).Error),
            ("UpdateTransaction", async s => (await s.GetRequiredService<UpdateTransactionHandler>().HandleAsync(
                portfolioId,
                assetId,
                transactionId,
                new UpdateTransactionRequest { Type = TransactionType.Deposit, Quantity = 50m, UnitPrice = 1m, Date = new DateOnly(2026, 1, 2) },
                cancellationToken)).Error),
            ("DeleteTransaction", async s => (await s.GetRequiredService<DeleteTransactionHandler>()
                .HandleAsync(portfolioId, assetId, transactionId, cancellationToken)).Error),
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
            Assert.Equal("Cash account", asset.Name);
            Assert.Equal(100m, asset.Quantity);
            Assert.Equal(100m, (await verifyDb.Transactions.SingleAsync(t => t.Id == transactionId, cancellationToken)).Quantity);
        }

        await using var remove = Provider.CreateAsyncScope();
        var removeResult = await remove.ServiceProvider.GetRequiredService<RemoveAssetHandler>()
            .HandleAsync(portfolioId, assetId, cancellationToken);

        Assert.True(removeResult.IsSuccess, removeResult.IsFailure ? removeResult.Error.Code : null);
        var removeDb = remove.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        Assert.False(await removeDb.Assets.AnyAsync(a => a.Id == assetId, cancellationToken));
        Assert.Equal(assetId, Assert.Single(await removeDb.ReadPublishedAsync<AssetRemoved>(cancellationToken)).AssetId);
    }
}
