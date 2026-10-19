using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.DeleteTransaction;
using Skarbiec.Portfolio.Features.RecordTransaction;
using Skarbiec.Portfolio.Features.UpdateAsset;
using Skarbiec.Portfolio.Features.UpdateTransaction;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

public sealed class AccountCashClassOutboxTests(SkarbiecContainersFixture containers) : PortfolioOutboxTestBase(containers)
{
    private static readonly DateOnly Day1 = new(2026, 1, 5);

    private static readonly DateOnly Day2 = new(2026, 1, 6);

    private static readonly DateOnly Day3 = new(2026, 1, 7);

    private static readonly DateOnly Funding = AccountsApi.FundingDay;

    [Fact]
    public async Task NoBuy_PublishesCash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var accountId = await CreateAccountAsync(scope.ServiceProvider, "XTB", cancellationToken);
        await FundAccountCashAsync(scope.ServiceProvider, accountId, cancellationToken);
        var cash = await LoadAccountCashAsync(scope.ServiceProvider, accountId, cancellationToken);

        var evt = await dbContext.LastPublishedAsync(cash.Id, cancellationToken);

        Assert.Equal(AssetClass.Cash, evt.AssetClass);
        Assert.Equal([(Funding, AssetClass.Cash)], evt.ClassPoints());
    }

    [Fact]
    public async Task LastBuy_SetsClass()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var accountId = await CreateAccountAsync(scope.ServiceProvider, "XTB", cancellationToken);
        await FundAccountCashAsync(scope.ServiceProvider, accountId, cancellationToken);
        var cash = await LoadAccountCashAsync(scope.ServiceProvider, accountId, cancellationToken);
        var stockId = await AddSecurityAsync(scope.ServiceProvider, accountId, AssetClass.Stock, "Stock", cancellationToken);
        var etfId = await AddSecurityAsync(scope.ServiceProvider, accountId, AssetClass.Etf, "Etf", cancellationToken);

        await RecordTradeAsync(scope.ServiceProvider, stockId, TransactionType.Buy, 5m, Day1, cancellationToken);
        await RecordTradeAsync(scope.ServiceProvider, etfId, TransactionType.Buy, 5m, Day2, cancellationToken);

        var evt = await dbContext.LastPublishedAsync(cash.Id, cancellationToken);
        Assert.Equal(AssetClass.Etf, evt.AssetClass);
        Assert.Equal([(Funding, AssetClass.Cash), (Day1, AssetClass.Stock), (Day2, AssetClass.Etf)], evt.ClassPoints());
    }

    [Fact]
    public async Task SellAll_ReturnsToCash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var accountId = await CreateAccountAsync(scope.ServiceProvider, "XTB", cancellationToken);
        await FundAccountCashAsync(scope.ServiceProvider, accountId, cancellationToken);
        var cash = await LoadAccountCashAsync(scope.ServiceProvider, accountId, cancellationToken);
        var stockId = await AddSecurityAsync(scope.ServiceProvider, accountId, AssetClass.Stock, "Stock", cancellationToken);
        await RecordTradeAsync(scope.ServiceProvider, stockId, TransactionType.Buy, 5m, Day1, cancellationToken);

        await RecordTradeAsync(scope.ServiceProvider, stockId, TransactionType.Sell, 5m, Day2, cancellationToken);

        var evt = await dbContext.LastPublishedAsync(cash.Id, cancellationToken);
        Assert.Equal(AssetClass.Cash, evt.AssetClass);
        Assert.Equal([(Funding, AssetClass.Cash), (Day1, AssetClass.Stock), (Day2, AssetClass.Cash)], evt.ClassPoints());
    }

    [Fact]
    public async Task BondsAccount_PublishesBond()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var accountId = await CreateAccountAsync(scope.ServiceProvider, "Obligacje", cancellationToken, AccountType.Bonds);
        var cash = await LoadAccountCashAsync(scope.ServiceProvider, accountId, cancellationToken);
        Assert.Equal(AssetClass.Bond, (await dbContext.LastPublishedAsync(cash.Id, cancellationToken)).AssetClass);

        await FundAccountCashAsync(scope.ServiceProvider, accountId, cancellationToken);

        var evt = await dbContext.LastPublishedAsync(cash.Id, cancellationToken);
        Assert.Equal(AssetClass.Bond, evt.AssetClass);
        Assert.Empty(evt.ClassHistory);
    }

    [Fact]
    public async Task EditedAndDeletedBuy_RecomputesTimeline()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var accountId = await CreateAccountAsync(scope.ServiceProvider, "XTB", cancellationToken);
        await FundAccountCashAsync(scope.ServiceProvider, accountId, cancellationToken);
        var cash = await LoadAccountCashAsync(scope.ServiceProvider, accountId, cancellationToken);
        var stockId = await AddSecurityAsync(scope.ServiceProvider, accountId, AssetClass.Stock, "Stock", cancellationToken);
        var etfId = await AddSecurityAsync(scope.ServiceProvider, accountId, AssetClass.Etf, "Etf", cancellationToken);
        var stockBuyId = await RecordTradeAsync(scope.ServiceProvider, stockId, TransactionType.Buy, 5m, Day2, cancellationToken);
        var etfBuyId = await RecordTradeAsync(scope.ServiceProvider, etfId, TransactionType.Buy, 5m, Day3, cancellationToken);

        var edited = await scope.ServiceProvider.GetRequiredService<UpdateTransactionHandler>().HandleAsync(
            etfId,
            etfBuyId,
            new UpdateTransactionRequest { Type = TransactionType.Buy, Quantity = 5m, UnitPrice = 10m, Date = Day1 },
            cancellationToken);
        Assert.True(edited.IsSuccess, edited.IsFailure ? edited.Error.Code : null);

        var afterEdit = await dbContext.LastPublishedAsync(cash.Id, cancellationToken);
        Assert.Equal(AssetClass.Stock, afterEdit.AssetClass);
        Assert.Equal([(Funding, AssetClass.Cash), (Day1, AssetClass.Etf), (Day2, AssetClass.Stock)], afterEdit.ClassPoints());

        var deleted = await scope.ServiceProvider.GetRequiredService<DeleteTransactionHandler>()
            .HandleAsync(stockId, stockBuyId, cancellationToken);
        Assert.True(deleted.IsSuccess, deleted.IsFailure ? deleted.Error.Code : null);

        var afterDelete = await dbContext.LastPublishedAsync(cash.Id, cancellationToken);
        Assert.Equal(AssetClass.Etf, afterDelete.AssetClass);
        Assert.Equal([(Funding, AssetClass.Cash), (Day1, AssetClass.Etf)], afterDelete.ClassPoints());
    }

    [Fact]
    public async Task UpdateAssetClass_RepublishesCash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var accountId = await CreateAccountAsync(scope.ServiceProvider, "XTB", cancellationToken);
        await FundAccountCashAsync(scope.ServiceProvider, accountId, cancellationToken);
        var cash = await LoadAccountCashAsync(scope.ServiceProvider, accountId, cancellationToken);
        var securityId = await AddSecurityAsync(scope.ServiceProvider, accountId, AssetClass.Stock, "Holding", cancellationToken);
        await RecordTradeAsync(scope.ServiceProvider, securityId, TransactionType.Buy, 5m, Day1, cancellationToken);
        var cashEventsBefore = (await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Count(e => e.AssetId == cash.Id);

        var updated = await scope.ServiceProvider.GetRequiredService<UpdateAssetHandler>().HandleAsync(
            securityId,
            new UpdateAssetRequest
            {
                AssetClass = AssetClass.Etf,
                Name = "Holding",
                Currency = "PLN",
                InstrumentId = FakeInstrumentLookupClient.IdFor(AssetClass.Etf, "PLN")
            },
            cancellationToken);
        Assert.True(updated.IsSuccess, updated.IsFailure ? updated.Error.Code : null);

        var events = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        Assert.Equal(cashEventsBefore + 1, events.Count(e => e.AssetId == cash.Id));
        var evt = events.Last(e => e.AssetId == cash.Id);
        Assert.Equal(AssetClass.Etf, evt.AssetClass);
        Assert.Equal([(Funding, AssetClass.Cash), (Day1, AssetClass.Etf)], evt.ClassPoints());
    }

    [Fact]
    public async Task OtherAssets_EmptyHistory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var brokerageId = await CreateAccountAsync(scope.ServiceProvider, "XTB", cancellationToken);
        var stockId = await AddAssetWithBuyAsync(scope.ServiceProvider, brokerageId, "Stock", cancellationToken);
        var bank = await CreateBankAccountAsync(scope.ServiceProvider, "Bank", "PLN", 1m, cancellationToken, balance: 100m);
        var placeBalanceId = await AddCashWithBalanceAsync(scope.ServiceProvider, 50m, cancellationToken);

        var stock = await dbContext.LastPublishedAsync(stockId, cancellationToken);
        var bankMoney = await dbContext.LastPublishedAsync(bank.CashAssetId, cancellationToken);
        var placeBalance = await dbContext.LastPublishedAsync(placeBalanceId, cancellationToken);

        Assert.Equal(AssetClass.Stock, stock.AssetClass);
        Assert.Equal(AssetClass.BankAccount, bankMoney.AssetClass);
        Assert.Equal(AssetClass.Cash, placeBalance.AssetClass);
        Assert.Empty(stock.ClassHistory);
        Assert.Empty(bankMoney.ClassHistory);
        Assert.Empty(placeBalance.ClassHistory);
    }
}
