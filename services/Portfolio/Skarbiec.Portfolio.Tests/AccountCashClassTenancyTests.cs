using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

public sealed class AccountCashClassTenancyTests(SkarbiecContainersFixture containers) : PortfolioOutboxTestBase(containers)
{
    private static readonly DateOnly Day1 = new(2026, 1, 5);

    private static readonly DateOnly Day2 = new(2026, 1, 6);

    [Fact]
    public async Task TimelineIgnoresOtherUsers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var strangerId = Guid.NewGuid();

        await using (var ownerScope = Provider.CreateAsyncScope())
        {
            var ownerAccountId = await CreateAccountAsync(ownerScope.ServiceProvider, "Owner XTB", cancellationToken);
            await FundAccountCashAsync(ownerScope.ServiceProvider, ownerAccountId, cancellationToken);
            var etfId = await AddSecurityAsync(ownerScope.ServiceProvider, ownerAccountId, AssetClass.Etf, "Etf", cancellationToken);
            await RecordTradeAsync(ownerScope.ServiceProvider, etfId, TransactionType.Buy, 5m, Day2, cancellationToken);
        }

        ActAs(strangerId);
        await using var strangerScope = Provider.CreateAsyncScope();
        var strangerAccountId = await CreateAccountAsync(strangerScope.ServiceProvider, "Stranger XTB", cancellationToken);
        await FundAccountCashAsync(strangerScope.ServiceProvider, strangerAccountId, cancellationToken);
        var strangerCash = await LoadAccountCashAsync(strangerScope.ServiceProvider, strangerAccountId, cancellationToken);
        var stockId = await AddSecurityAsync(strangerScope.ServiceProvider, strangerAccountId, AssetClass.Stock, "Stock", cancellationToken);

        await RecordTradeAsync(strangerScope.ServiceProvider, stockId, TransactionType.Buy, 5m, Day1, cancellationToken);

        var evt = await strangerScope.ServiceProvider.GetRequiredService<PortfolioDbContext>()
            .LastPublishedAsync(strangerCash.Id, cancellationToken);
        Assert.Equal(AssetClass.Stock, evt.AssetClass);
        Assert.Equal([(AccountsApi.FundingDay, AssetClass.Cash), (Day1, AssetClass.Stock)], evt.ClassPoints());
    }
}
