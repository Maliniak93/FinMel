using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.ArchivePortfolio;
using Skarbiec.Portfolio.Features.CreatePortfolio;
using Skarbiec.Portfolio.Features.Deposits.AddDeposit;
using Skarbiec.Portfolio.Features.Deposits.RollOverDeposit;
using Skarbiec.Portfolio.Features.Deposits.SettleDeposit;
using Skarbiec.Portfolio.Features.Deposits.UpdateDeposit;
using Skarbiec.Portfolio.Features.RemoveAsset;
using Skarbiec.Portfolio.Features.UpdateAsset;
using Skarbiec.Portfolio.MarketData;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class DepositOutboxTests(SkarbiecContainersFixture containers) : PortfolioOutboxTestBase(containers)
{
    [Fact]
    public async Task AddDeposit_PublishesPositionChangedWithPrincipal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
        var portfolioId = (await scope.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = "Deposit outbox portfolio" }, cancellationToken)).Value.Id;

        SaveChanges.Reset();

        var result = await scope.ServiceProvider.GetRequiredService<AddDepositHandler>()
            .HandleAsync(portfolioId, PortfolioApi.NewDepositRequest(), cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, SaveChanges.Count);
        var assetId = result.Value.AssetId;

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var asset = await verifyDb.Assets.SingleAsync(a => a.Id == assetId, cancellationToken);
        Assert.Equal(AssetClass.Deposit, asset.AssetClass);
        Assert.Equal(10_000m, asset.Quantity);
        Assert.Equal(1, await verifyDb.Transactions.CountAsync(t => t.AssetId == assetId, cancellationToken));
        Assert.True(await verifyDb.Set<TermDeposit>().AnyAsync(t => t.AssetId == assetId, cancellationToken));

        var evt = Assert.Single(await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken));
        Assert.Equal(assetId, evt.AssetId);
        Assert.Equal(portfolioId, evt.PortfolioId);
        Assert.Equal(UserId, evt.UserId);
        Assert.Equal(AssetClass.Deposit, evt.AssetClass);
        Assert.Equal(AssetValuationMode.CurrencyValued, evt.ValuationMode);
        Assert.Equal("PLN", evt.Currency);
        Assert.Equal(10_000m, evt.Quantity);
        Assert.Null(evt.InstrumentId);
        Assert.Null(evt.ManualValueAmount);
        Assert.False(evt.PortfolioIsArchived);
    }

    [Fact]
    public async Task UpdateDeposit_PublishesOnePositionChangedWithNewQuantity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid portfolioId;
        Guid assetId;
        await using (var arrange = Provider.CreateAsyncScope())
        {
            portfolioId = (await arrange.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
                .HandleAsync(new CreatePortfolioRequest { Name = "Deposit outbox portfolio" }, cancellationToken)).Value.Id;
            var added = await arrange.ServiceProvider.GetRequiredService<AddDepositHandler>()
                .HandleAsync(portfolioId, PortfolioApi.NewDepositRequest(), cancellationToken);
            Assert.True(added.IsSuccess);
            assetId = added.Value.AssetId;
        }

        SaveChanges.Reset();

        await using (var act = Provider.CreateAsyncScope())
        {
            var request = PortfolioApi.NewDepositRequest(principal: 15_000m, startDate: new DateOnly(2026, 2, 1), termLength: 6)
                .ToUpdateRequest();
            var result = await act.ServiceProvider.GetRequiredService<UpdateDepositHandler>()
                .HandleAsync(portfolioId, assetId, request, cancellationToken);
            Assert.True(result.IsSuccess);
        }

        Assert.Equal(1, SaveChanges.Count);

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var events = await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal(assetId, e.AssetId));
        Assert.Equal(10_000m, events[0].Quantity);
        Assert.Equal(15_000m, events[1].Quantity);
        Assert.True(events[1].Version > events[0].Version);
        Assert.Equal(AssetClass.Deposit, events[1].AssetClass);

        var opening = await verifyDb.Transactions.SingleAsync(t => t.AssetId == assetId, cancellationToken);
        Assert.Equal(15_000m, opening.Quantity);
        Assert.Equal(new DateOnly(2026, 2, 1), opening.Date);
        Assert.Equal(15_000m, (await verifyDb.Assets.SingleAsync(a => a.Id == assetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task SettleDeposit_PublishesPositionChangedWithFinalAmount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid portfolioId;
        Guid assetId;
        await using (var arrange = Provider.CreateAsyncScope())
        {
            portfolioId = (await arrange.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
                .HandleAsync(new CreatePortfolioRequest { Name = "Settlement outbox portfolio" }, cancellationToken)).Value.Id;
            var added = await arrange.ServiceProvider.GetRequiredService<AddDepositHandler>()
                .HandleAsync(portfolioId, PortfolioApi.NewDepositRequest(), cancellationToken);
            Assert.True(added.IsSuccess);
            assetId = added.Value.AssetId;
        }

        SaveChanges.Reset();

        await using (var act = Provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<SettleDepositHandler>()
                .HandleAsync(portfolioId, assetId, PortfolioApi.NewSettleRequest(), cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, SaveChanges.Count);

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var events = await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal(assetId, e.AssetId));
        Assert.Equal(10_000m, events[0].Quantity);
        Assert.Equal(10_119.83m, events[1].Quantity);
        Assert.True(events[1].Version > events[0].Version);
        Assert.Equal(AssetClass.Deposit, events[1].AssetClass);
        Assert.Equal(UserId, events[1].UserId);

        Assert.Equal(10_119.83m, (await verifyDb.Assets.SingleAsync(a => a.Id == assetId, cancellationToken)).Quantity);
        var credit = await verifyDb.Transactions.SingleAsync(t => t.AssetId == assetId && t.Quantity == 119.83m, cancellationToken);
        Assert.Equal(TransactionType.Deposit, credit.Type);
        Assert.Equal(new DateOnly(2026, 4, 15), credit.Date);
        var terms = await verifyDb.Set<TermDeposit>().SingleAsync(t => t.AssetId == assetId, cancellationToken);
        Assert.Equal(new DateOnly(2026, 4, 15), terms.SettledOn);
        Assert.Equal(147.95m, terms.SettledGrossInterest);
        Assert.Equal(28.12m, terms.SettledTax);
    }

    [Fact]
    public async Task RemoveAsset_TermDeposit_WritesAssetRemovedAndDeletesTermsInOneSave()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid portfolioId;
        Guid assetId;
        await using (var arrange = Provider.CreateAsyncScope())
        {
            portfolioId = (await arrange.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
                .HandleAsync(new CreatePortfolioRequest { Name = "Deposit outbox portfolio" }, cancellationToken)).Value.Id;
            var added = await arrange.ServiceProvider.GetRequiredService<AddDepositHandler>()
                .HandleAsync(portfolioId, PortfolioApi.NewDepositRequest(), cancellationToken);
            Assert.True(added.IsSuccess);
            assetId = added.Value.AssetId;
        }

        SaveChanges.Reset();

        await using (var act = Provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<RemoveAssetHandler>()
                .HandleAsync(portfolioId, assetId, cancellationToken);
            Assert.True(result.IsSuccess);
        }

        Assert.Equal(1, SaveChanges.Count);

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        Assert.False(await verifyDb.Assets.AnyAsync(a => a.Id == assetId, cancellationToken));
        Assert.False(await verifyDb.Transactions.AnyAsync(t => t.AssetId == assetId, cancellationToken));
        Assert.False(await verifyDb.Set<TermDeposit>().AnyAsync(t => t.AssetId == assetId, cancellationToken));

        var evt = Assert.Single(await verifyDb.ReadPublishedAsync<AssetRemoved>(cancellationToken));
        Assert.Equal(assetId, evt.AssetId);
        Assert.Equal(portfolioId, evt.PortfolioId);
        Assert.Equal(UserId, evt.UserId);
        Assert.False(evt.CascadedFromPortfolio);
    }

    [Fact]
    public async Task DepositWriteToArchivedPortfolio_WritesNoEvent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid portfolioId;
        Guid assetId;
        await using (var arrange = Provider.CreateAsyncScope())
        {
            portfolioId = (await arrange.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
                .HandleAsync(new CreatePortfolioRequest { Name = "Archived deposit portfolio" }, cancellationToken)).Value.Id;
            var added = await arrange.ServiceProvider.GetRequiredService<AddDepositHandler>()
                .HandleAsync(portfolioId, PortfolioApi.NewDepositRequest(), cancellationToken);
            Assert.True(added.IsSuccess);
            assetId = added.Value.AssetId;
            Assert.True((await arrange.ServiceProvider.GetRequiredService<ArchivePortfolioHandler>()
                .HandleAsync(portfolioId, cancellationToken)).IsSuccess);
        }

        int outboxRowsBefore;
        await using (var before = Provider.CreateAsyncScope())
        {
            outboxRowsBefore = await before.ServiceProvider.GetRequiredService<PortfolioDbContext>()
                .Set<OutboxMessage>().CountAsync(cancellationToken);
        }

        var writes = new (string Name, Func<IServiceProvider, Task<Error>> Write)[]
        {
            ("AddDeposit", async s => (await s.GetRequiredService<AddDepositHandler>().HandleAsync(
                portfolioId, PortfolioApi.NewDepositRequest(name: "Another"), cancellationToken)).Error),
            ("UpdateDeposit", async s => (await s.GetRequiredService<UpdateDepositHandler>().HandleAsync(
                portfolioId,
                assetId,
                PortfolioApi.NewDepositRequest(principal: 99_999m).ToUpdateRequest(),
                cancellationToken)).Error),
            ("SettleDeposit", async s => (await s.GetRequiredService<SettleDepositHandler>().HandleAsync(
                portfolioId, assetId, PortfolioApi.NewSettleRequest(), cancellationToken)).Error),
        };

        foreach (var (name, write) in writes)
        {
            await using var scope = Provider.CreateAsyncScope();
            var error = await write(scope.ServiceProvider);
            Assert.True(
                error.Code == PortfolioAssertions.PortfolioArchivedErrorCode,
                $"{name} on an archived portfolio returned '{error.Code}', expected '{PortfolioAssertions.PortfolioArchivedErrorCode}'.");
        }

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        Assert.Equal(outboxRowsBefore, await verifyDb.Set<OutboxMessage>().CountAsync(cancellationToken));
        Assert.Equal(1, await verifyDb.Assets.CountAsync(a => a.PortfolioId == portfolioId, cancellationToken));
        Assert.Equal(10_000m, (await verifyDb.Set<TermDeposit>().SingleAsync(t => t.AssetId == assetId, cancellationToken)).Principal);
    }

    [Fact]
    public async Task RollOverDueDeposit_PublishesPosition()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid portfolioId;
        Guid assetId;
        await using (var arrange = Provider.CreateAsyncScope())
        {
            portfolioId = await CreatePortfolioAsync(arrange.ServiceProvider, "Rollover outbox portfolio", cancellationToken);
            var added = await arrange.ServiceProvider.GetRequiredService<AddDepositHandler>()
                .HandleAsync(portfolioId, PortfolioApi.NewDepositRequest(), cancellationToken);
            Assert.True(added.IsSuccess, added.IsFailure ? added.Error.Code : null);
            assetId = added.Value.AssetId;
        }

        IReadOnlyList<AssetPositionChanged> eventsBefore;
        await using (var before = Provider.CreateAsyncScope())
        {
            eventsBefore = await before.ServiceProvider.GetRequiredService<PortfolioDbContext>()
                .ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        }

        SaveChanges.Reset();

        await using (var act = Provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<RollOverDepositHandler>()
                .HandleAsync(portfolioId, assetId, PortfolioApi.NewRollOverRequest(), cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, SaveChanges.Count);

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var events = (await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(eventsBefore.Count).ToList();
        var evt = Assert.Single(events);
        Assert.Equal(assetId, evt.AssetId);
        Assert.Equal(10_119.83m, evt.Quantity);
        Assert.Equal(AssetClass.Deposit, evt.AssetClass);
        Assert.Equal(UserId, evt.UserId);
        Assert.True(evt.Version > eventsBefore.Where(e => e.AssetId == assetId).Max(e => e.Version));

        Assert.Equal(10_119.83m, (await verifyDb.Assets.SingleAsync(a => a.Id == assetId, cancellationToken)).Quantity);
        var credit = await verifyDb.Transactions.SingleAsync(t => t.AssetId == assetId && t.Quantity == 119.83m, cancellationToken);
        Assert.Equal(TransactionType.Deposit, credit.Type);
        Assert.Equal(new DateOnly(2026, 4, 15), credit.Date);
        var terms = await verifyDb.Set<TermDeposit>().SingleAsync(t => t.AssetId == assetId, cancellationToken);
        Assert.Equal(1, terms.RolloverCount);
        Assert.Equal(10_119.83m, terms.Principal);
        Assert.Equal(new DateOnly(2026, 4, 15), terms.StartDate);
        Assert.Equal(new DateOnly(2026, 7, 15), terms.MaturityDate);
        Assert.Equal(5.5m, terms.AnnualInterestRatePercent);
        Assert.Null(terms.SettledOn);
        Assert.Null(terms.SettledGrossInterest);
        Assert.Null(terms.SettledTax);
    }

    [Fact]
    public async Task RollOverSettledDeposit_PublishesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid portfolioId;
        Guid assetId;
        await using (var arrange = Provider.CreateAsyncScope())
        {
            portfolioId = await CreatePortfolioAsync(arrange.ServiceProvider, "Rollover outbox portfolio", cancellationToken);
            var added = await arrange.ServiceProvider.GetRequiredService<AddDepositHandler>()
                .HandleAsync(portfolioId, PortfolioApi.NewDepositRequest(), cancellationToken);
            Assert.True(added.IsSuccess, added.IsFailure ? added.Error.Code : null);
            assetId = added.Value.AssetId;
            var settled = await arrange.ServiceProvider.GetRequiredService<SettleDepositHandler>()
                .HandleAsync(portfolioId, assetId, PortfolioApi.NewSettleRequest(), cancellationToken);
            Assert.True(settled.IsSuccess, settled.IsFailure ? settled.Error.Code : null);
        }

        int outboxRowsBefore;
        int transactionsBefore;
        await using (var before = Provider.CreateAsyncScope())
        {
            var beforeDb = before.ServiceProvider.GetRequiredService<PortfolioDbContext>();
            outboxRowsBefore = await beforeDb.Set<OutboxMessage>().CountAsync(cancellationToken);
            transactionsBefore = await beforeDb.Transactions.CountAsync(cancellationToken);
        }

        SaveChanges.Reset();

        await using (var act = Provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<RollOverDepositHandler>()
                .HandleAsync(portfolioId, assetId, PortfolioApi.NewRollOverRequest(grossInterest: null, tax: null), cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, SaveChanges.Count);

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        Assert.Equal(outboxRowsBefore, await verifyDb.Set<OutboxMessage>().CountAsync(cancellationToken));
        Assert.Equal(transactionsBefore, await verifyDb.Transactions.CountAsync(cancellationToken));
        Assert.Equal(10_119.83m, (await verifyDb.Assets.SingleAsync(a => a.Id == assetId, cancellationToken)).Quantity);
        var terms = await verifyDb.Set<TermDeposit>().SingleAsync(t => t.AssetId == assetId, cancellationToken);
        Assert.Equal(1, terms.RolloverCount);
        Assert.Equal(10_119.83m, terms.Principal);
        Assert.Equal(new DateOnly(2026, 4, 15), terms.StartDate);
        Assert.Null(terms.SettledOn);
    }
}
