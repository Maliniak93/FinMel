using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class PlanningYearTenancyIsolationTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Read_ByStranger_SeesOnlyOwnMonths()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await owner.CreateHouseholdIncomesAsync(cancellationToken);

        var ownerYear = await owner.GetPlanningYearAsync(2026, cancellationToken);
        var strangerYear = await stranger.GetPlanningYearAsync(2026, cancellationToken);

        Assert.Equal(16_500m, ownerYear.Months.Single(m => m.Month == "2026-10").IncomePlanPln);
        Assert.All(strangerYear.Months, m => Assert.False(m.HasDefinitions));
        Assert.All(strangerYear.Months, m => Assert.Null(m.IncomePlanPln));
        Assert.Equal((2026, 2027), (strangerYear.FirstYear, strangerYear.LastYear));
        Assert.Equal(0m, strangerYear.Totals.IncomePlanPln);
    }
}
