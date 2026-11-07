using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class ContributionLimitTenancyIsolationTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task StrangerSeesAndTouchesNothingOfTheOwnersLimits()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        await owner.SetContributionLimitsAsync(2026, 28_260m, 11_304m, cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        Assert.Empty(await stranger.ListContributionLimitsAsync(cancellationToken));
        var delete = await stranger.DeleteAsync(ContributionLimitsYearUri(2026), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);

        var set = await stranger.PutAsJsonAsync(
            ContributionLimitsYearUri(2026), new { ikeAmount = 1m, ikzeAmount = 2m }, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);

        Assert.Equal(
            new ContributionLimitsView(2026, 28_260m, 11_304m),
            Assert.Single(await owner.ListContributionLimitsAsync(cancellationToken)));
        Assert.Equal(
            new ContributionLimitsView(2026, 1m, 2m),
            Assert.Single(await stranger.ListContributionLimitsAsync(cancellationToken)));
        await using var dbContext = CreateDbContext(ownerId);
        Assert.Equal(4, await dbContext.Set<ContributionLimit>().IgnoreQueryFilters().CountAsync(cancellationToken));
        Assert.Equal(2, await dbContext.Set<ContributionLimit>().CountAsync(cancellationToken));
    }
}
