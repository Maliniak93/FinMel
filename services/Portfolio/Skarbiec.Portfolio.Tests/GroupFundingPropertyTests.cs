using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AssetGroupsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.GroupFundingApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class GroupFundingPropertyTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task RealisationInvariantsHoldForRandomScenarios()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        var random = new Random(843);

        for (var round = 0; round < 8; round++)
        {
            using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
            var income = await client.CreateIncomeAccountAsync(cancellationToken, "A", balance: 100_000m);
            var other = await client.CreateBankAccountInGroupAsync(cancellationToken, null, "B", balance: 100_000m);

            var contributions = new List<ContributionBody>();
            if (random.Next(2) == 0)
            {
                contributions.Add(new ContributionBody(Hundreds(random), income.AccountId));
            }

            if (random.Next(2) == 0)
            {
                contributions.Add(new ContributionBody(Hundreds(random), other.AccountId));
            }

            if (random.Next(2) == 0)
            {
                contributions.Add(new ContributionBody(Hundreds(random), null));
            }

            var groupId = await client.CreateFundingGroupAsync(
                cancellationToken, "Grupa", contributions.Sum(c => c.AmountPln),
                contributions: contributions.Count == 0 ? null : [.. contributions]);
            var target = await client.CreateBankAccountInGroupAsync(cancellationToken, groupId, "Cel");

            var transfers = random.Next(1, 4);
            for (var i = transfers; i > 0; i--)
            {
                var source = random.Next(2) == 0 ? income : other;
                await client.TransferMoneyAsync(source.MoneyAssetId, target.MoneyAssetId, Hundreds(random), FundingDay, cancellationToken);
            }

            var expenses = (await client.GetPlanningMonthAsync(CurrentMonth, cancellationToken)).Expenses;
            var contribution = expenses.GroupContributions?.SingleOrDefault(c => c.GroupId == groupId);
            var deposit = expenses.GroupDeposits?.SingleOrDefault(d => d.GroupId == groupId);
            var funded = contribution?.FundedPln ?? 0m;

            Assert.True(funded >= 0m && funded <= (contribution?.AmountPln ?? 0m));
            Assert.NotNull(deposit);
            Assert.True(deposit.ExcessPln >= 0m);
            Assert.True(deposit.FromIncomePln <= deposit.TotalPln);
            Assert.True(funded + deposit.ExcessPln <= deposit.TotalPln);
        }
    }

    private static decimal Hundreds(Random random) => random.Next(1, 30) * 100m;
}
