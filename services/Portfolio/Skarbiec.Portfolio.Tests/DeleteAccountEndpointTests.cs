using System.Net;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;

namespace Skarbiec.Portfolio.Tests;

// The DELETE under test is called directly and asserted on the raw response; AccountsApi helpers only arrange.
public sealed class DeleteAccountEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Delete_AccountWithAnAsset_ReturnsAccountHasHistory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);
        await client.AddStockInAccountAsync(accountId, cancellationToken);

        var response = await client.DeleteAsync(AccountUri(accountId), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.AccountHasHistoryErrorCode, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(AccountUri(accountId), cancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Delete_EmptyAccount_ReturnsNoContentAndRemovesIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);

        var response = await client.DeleteAsync(AccountUri(accountId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(AccountUri(accountId), cancellationToken)).StatusCode);
    }
}
