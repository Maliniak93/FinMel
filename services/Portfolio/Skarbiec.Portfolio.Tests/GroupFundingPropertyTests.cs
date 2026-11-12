using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.GroupFundingApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class GroupFundingPropertyTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task NetInvariantsHoldForRandomScenarios()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        var random = new Random(864);

        for (var round = 0; round < 8; round++)
        {
            using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
            var income = await client.CreateIncomeAccountAsync(cancellationToken, "A", balance: 100_000m);
            var other = await client.CreateBankAccountInGroupAsync(cancellationToken, null, "B", balance: 100_000m);
            var groupId = await client.CreateFundingGroupAsync(cancellationToken, "Grupa", 1_000m);
            var target = await client.CreateBankAccountInGroupAsync(cancellationToken, groupId, "Cel", balance: 100_000m);
            var sibling = await client.CreateBankAccountInGroupAsync(cancellationToken, groupId, "Rodzeństwo");

            var expected = 0m;
            for (var i = random.Next(2, 7); i > 0; i--)
            {
                var amount = Hundreds(random);
                switch (random.Next(5))
                {
                    case 0:
                        await client.TransferMoneyAsync(income.MoneyAssetId, target.MoneyAssetId, amount, FundingDay, cancellationToken);
                        expected += amount;
                        break;
                    case 1:
                        await client.TransferMoneyAsync(other.MoneyAssetId, sibling.MoneyAssetId, amount, FundingDay, cancellationToken);
                        expected += amount;
                        break;
                    case 2:
                        await client.TransferMoneyAsync(target.MoneyAssetId, other.MoneyAssetId, amount, FundingDay, cancellationToken);
                        expected -= amount;
                        break;
                    case 3:
                        await client.TransferMoneyAsync(target.MoneyAssetId, sibling.MoneyAssetId, amount, FundingDay, cancellationToken);
                        break;
                    default:
                        await client.DepositMoneyAsync(target.MoneyAssetId, amount, FundingDay, cancellationToken);
                        expected += amount;
                        break;
                }
            }

            var month = await client.GetPlanningMonthAsync(CurrentMonth, cancellationToken);

            Assert.Equal(expected, Assert.Single(month.Expenses.GroupContributions!, c => c.GroupId == groupId).NetPln);
        }
    }

    private static decimal Hundreds(Random random) => random.Next(1, 30) * 100m;
}
