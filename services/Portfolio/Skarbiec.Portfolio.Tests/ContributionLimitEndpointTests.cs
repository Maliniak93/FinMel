using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioAssertions;

namespace Skarbiec.Portfolio.Tests;

// Limit reads and writes are under test, so the requests are sent directly; AccountsApi helpers only arrange.
public sealed class ContributionLimitEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task SetListReplaceAndClear()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var set = await client.PutAsJsonAsync(
            ContributionLimitsYearUri(2026), new { ikeAmount = 28_260m, ikzeAmount = 11_304m }, cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);
        var listed = Assert.Single(await client.ListContributionLimitsAsync(cancellationToken));
        Assert.Equal(new ContributionLimitsView(2026, 28_260m, 11_304m), listed);

        var replace = await client.PutAsJsonAsync(
            ContributionLimitsYearUri(2026), new { ikeAmount = 30_000m, ikzeAmount = 12_000m }, cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, replace.StatusCode);
        Assert.Equal(
            new ContributionLimitsView(2026, 30_000m, 12_000m),
            Assert.Single(await client.ListContributionLimitsAsync(cancellationToken)));

        var clearIkze = await client.PutAsJsonAsync(
            ContributionLimitsYearUri(2026), new { ikeAmount = 30_000m, ikzeAmount = (decimal?)null }, cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, clearIkze.StatusCode);
        Assert.Equal(
            new ContributionLimitsView(2026, 30_000m, null),
            Assert.Single(await client.ListContributionLimitsAsync(cancellationToken)));

        await client.SetContributionLimitsAsync(2025, 1_000m, null, cancellationToken);
        var years = (await client.ListContributionLimitsAsync(cancellationToken)).Select(l => l.Year).ToList();
        Assert.Equal([2026, 2025], years);

        var clearBoth = await client.PutAsJsonAsync(
            ContributionLimitsYearUri(2026), new { ikeAmount = (decimal?)null, ikzeAmount = (decimal?)null }, cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, clearBoth.StatusCode);
        Assert.Equal(2025, Assert.Single(await client.ListContributionLimitsAsync(cancellationToken)).Year);
    }

    [Fact]
    public async Task Rejections()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);

        var zeroIke = await client.PutAsJsonAsync(
            ContributionLimitsYearUri(2026), new { ikeAmount = 0m, ikzeAmount = 11_304m }, cancellationToken);
        var negativeIkze = await client.PutAsJsonAsync(
            ContributionLimitsYearUri(2026), new { ikeAmount = 28_260m, ikzeAmount = -1m }, cancellationToken);
        var yearBeforeRange = await client.PutAsJsonAsync(
            ContributionLimitsYearUri(1999), new { ikeAmount = 28_260m }, cancellationToken);
        var yearAfterRange = await client.PutAsJsonAsync(
            ContributionLimitsYearUri(2101), new { ikeAmount = 28_260m }, cancellationToken);

        await zeroIke.AssertProblemAsync(HttpStatusCode.BadRequest, ContributionLimitErrorCode, cancellationToken);
        await negativeIkze.AssertProblemAsync(HttpStatusCode.BadRequest, ContributionLimitErrorCode, cancellationToken);
        await yearBeforeRange.AssertProblemAsync(HttpStatusCode.BadRequest, ContributionLimitErrorCode, cancellationToken);
        await yearAfterRange.AssertProblemAsync(HttpStatusCode.BadRequest, ContributionLimitErrorCode, cancellationToken);
        Assert.Empty(await client.ListContributionLimitsAsync(cancellationToken));
        await using var dbContext = CreateDbContext(userId);
        Assert.Empty(await dbContext.Set<ContributionLimit>().ToListAsync(cancellationToken));
    }

    [Fact]
    public async Task DeleteYear()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.SetContributionLimitsAsync(2026, 28_260m, 11_304m, cancellationToken);
        await client.SetContributionLimitsAsync(2025, 26_019m, null, cancellationToken);

        var delete = await client.DeleteAsync(ContributionLimitsYearUri(2026), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(2025, Assert.Single(await client.ListContributionLimitsAsync(cancellationToken)).Year);
        var deleteAgain = await client.DeleteAsync(ContributionLimitsYearUri(2026), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, deleteAgain.StatusCode);
    }
}
