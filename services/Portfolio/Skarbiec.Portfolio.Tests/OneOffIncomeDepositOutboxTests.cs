using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.OneOffIncomes.CreateOneOffIncome;
using Skarbiec.Portfolio.Features.OneOffIncomes.SettleOneOffIncome;
using Skarbiec.Portfolio.Features.OneOffIncomes.UndoOneOffIncomeSettlement;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

public sealed class OneOffIncomeDepositOutboxTests(SkarbiecContainersFixture containers) : PortfolioOutboxTestBase(containers)
{
    [Fact]
    public async Task SettleEditUndo_PublishPositionChanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var first = await CreateBankAccountAsync(scope.ServiceProvider, "mBank ROR", "PLN", 0m, cancellationToken);
        var second = await CreateBankAccountAsync(scope.ServiceProvider, "Konto firmowe", "PLN", 0m, cancellationToken);
        var created = await scope.ServiceProvider.GetRequiredService<CreateOneOffIncomeHandler>().HandleAsync(
            new CreateOneOffIncomeRequest
            {
                Name = "Premia",
                Amount = 1_000m,
                AccountId = first.Id,
                Date = new DateOnly(2026, 1, 10)
            },
            cancellationToken);
        Assert.True(created.IsSuccess);
        var incomeId = created.Value.Id;
        var settle = scope.ServiceProvider.GetRequiredService<SettleOneOffIncomeHandler>();
        var published = await CountPositionEventsAsync(cancellationToken);
        SaveChanges.Reset();

        var settled = await settle.HandleAsync(incomeId, Request(first.Id, 990m, new DateOnly(2026, 1, 10)), cancellationToken);

        Assert.True(settled.IsSuccess, settled.IsFailure ? settled.Error.Code : null);
        Assert.Equal(1, SaveChanges.Count);
        Assert.True(await dbContext.OneOffIncomes.AnyAsync(o => o.Id == incomeId && o.ActualAmount == 990m, cancellationToken));
        var settleEvents = (await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(published).ToList();
        var settleEvent = Assert.Single(settleEvents);
        Assert.Equal(first.CashAssetId, settleEvent.AssetId);
        Assert.Equal(990m, settleEvent.Quantity);
        published += settleEvents.Count;
        SaveChanges.Reset();

        var moved = await settle.HandleAsync(incomeId, Request(second.Id, 900m, new DateOnly(2026, 1, 12)), cancellationToken);

        Assert.True(moved.IsSuccess, moved.IsFailure ? moved.Error.Code : null);
        Assert.Equal(1, SaveChanges.Count);
        var moveEvents = (await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(published).ToList();
        Assert.Equal(2, moveEvents.Count);
        Assert.Equal(0m, Assert.Single(moveEvents, e => e.AssetId == first.CashAssetId).Quantity);
        Assert.Equal(900m, Assert.Single(moveEvents, e => e.AssetId == second.CashAssetId).Quantity);
        published += moveEvents.Count;
        SaveChanges.Reset();

        var undone = await scope.ServiceProvider.GetRequiredService<UndoOneOffIncomeSettlementHandler>()
            .HandleAsync(incomeId, cancellationToken);

        Assert.True(undone.IsSuccess, undone.IsFailure ? undone.Error.Code : null);
        Assert.Equal(1, SaveChanges.Count);
        Assert.True(await dbContext.OneOffIncomes.AnyAsync(o => o.Id == incomeId && o.ActualAmount == null, cancellationToken));
        var undoEvent = Assert.Single((await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(published));
        Assert.Equal(second.CashAssetId, undoEvent.AssetId);
        Assert.Equal(0m, undoEvent.Quantity);
    }

    private static SettleOneOffIncomeRequest Request(Guid accountId, decimal amount, DateOnly date) => new()
    {
        ActualAmount = amount,
        ActualDate = date,
        AccountId = accountId,
        CreateDeposit = true
    };
}
