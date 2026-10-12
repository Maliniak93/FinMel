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

// Limit writes are under test, so the PUT and DELETE are posted directly; AccountsApi helpers only arrange.
public sealed class AccountContributionLimitEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task SetReplaceDelete()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var accountId = await client.CreateAccountAsync(cancellationToken, "XTB IKE", wrapper: TaxWrapper.Ike);

        var set = await client.PutAsJsonAsync(ContributionLimitUri(accountId, 2026), new { amount = 26_000m }, cancellationToken);
        var replace = await client.PutAsJsonAsync(ContributionLimitUri(accountId, 2026), new { amount = 25_000m }, cancellationToken);

        Assert.True(set.IsSuccessStatusCode);
        Assert.True(replace.IsSuccessStatusCode);
        Assert.Equal(25_000m, (await client.GetContributionAsync(accountId, 2026, cancellationToken))!.LimitPln);
        await using var dbContext = CreateDbContext(userId);
        var limit = Assert.Single(
            await dbContext.AccountContributionLimits
                .Where(l => l.AccountId == accountId && l.Year == 2026)
                .ToListAsync(cancellationToken));
        Assert.Equal(25_000m, limit.Amount);

        var delete = await client.DeleteAsync(ContributionLimitUri(accountId, 2026), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Null((await client.GetContributionAsync(accountId, 2026, cancellationToken))!.LimitPln);
        var deleteAgain = await client.DeleteAsync(ContributionLimitUri(accountId, 2026), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, deleteAgain.StatusCode);
    }

    [Fact]
    public async Task Rejections()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var regular = await client.CreateAccountAsync(cancellationToken, "XTB zwykly");
        var ike = await client.CreateAccountAsync(cancellationToken, "XTB IKE", wrapper: TaxWrapper.Ike);

        var onRegular = await client.PutAsJsonAsync(ContributionLimitUri(regular, 2026), new { amount = 26_000m }, cancellationToken);
        var zeroAmount = await client.PutAsJsonAsync(ContributionLimitUri(ike, 2026), new { amount = 0m }, cancellationToken);
        var yearBeforeRange = await client.PutAsJsonAsync(ContributionLimitUri(ike, 1999), new { amount = 26_000m }, cancellationToken);

        await onRegular.AssertProblemAsync(HttpStatusCode.BadRequest, ContributionLimitNotAllowedErrorCode, cancellationToken);
        await zeroAmount.AssertProblemAsync(HttpStatusCode.BadRequest, ContributionLimitErrorCode, cancellationToken);
        await yearBeforeRange.AssertProblemAsync(HttpStatusCode.BadRequest, ContributionLimitErrorCode, cancellationToken);
    }

    [Fact]
    public async Task Set_OnArchivedAccount_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken, "XTB IKE", wrapper: TaxWrapper.Ike);
        await client.ArchiveAccountAsync(accountId, cancellationToken);

        var response = await client.PutAsJsonAsync(ContributionLimitUri(accountId, 2026), new { amount = 26_000m }, cancellationToken);

        await response.AssertAccountArchivedConflictAsync(cancellationToken);
    }
}
