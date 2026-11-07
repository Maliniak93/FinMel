using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class ContributionLimitAccountReadTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task AccountsTakeLimitOfTheirKind()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var ikeSmall = await client.CreateAccountWithCashAsync(cancellationToken, 1_000m, "IKE A", wrapper: TaxWrapper.Ike);
        var ikeBig = await client.CreateAccountWithCashAsync(cancellationToken, 5_000m, "IKE B", wrapper: TaxWrapper.Ike);
        var ikze = await client.CreateAccountWithCashAsync(cancellationToken, 2_000m, "IKZE", wrapper: TaxWrapper.Ikze);
        var regular = await client.CreateAccountWithCashAsync(cancellationToken, 3_000m, "Zwykle");
        await client.SetContributionLimitsAsync(2026, 28_260m, 11_304m, cancellationToken);

        var list = await client.GetFromJsonAsync<JsonArray>($"{AccountsUri}?year=2026", cancellationToken);

        Assert.Equal((1_000m, 28_260m), await ReadFromListAsync(list!, ikeSmall.AccountId));
        Assert.Equal((5_000m, 28_260m), await ReadFromListAsync(list!, ikeBig.AccountId));
        Assert.Equal((2_000m, 11_304m), await ReadFromListAsync(list!, ikze.AccountId));
        Assert.Null(ContributionOf(list!, regular.AccountId)?.LimitPln);
        Assert.Equal((1_000m, 28_260m), await GetAsync(client, ikeSmall.AccountId, 2026, cancellationToken));
        Assert.Equal((5_000m, 28_260m), await GetAsync(client, ikeBig.AccountId, 2026, cancellationToken));
        Assert.Equal((2_000m, 11_304m), await GetAsync(client, ikze.AccountId, 2026, cancellationToken));
        Assert.Null((await client.GetContributionAsync(regular.AccountId, 2026, cancellationToken))?.LimitPln);
        Assert.Null((await client.GetContributionAsync(ikeSmall.AccountId, 2025, cancellationToken))!.LimitPln);
    }

    private static Task<(decimal, decimal?)> ReadFromListAsync(JsonArray list, Guid accountId)
    {
        var view = ContributionOf(list, accountId)!;

        return Task.FromResult((view.ContributedPln, view.LimitPln));
    }

    private static ContributionView? ContributionOf(JsonArray list, Guid accountId) =>
        list.Single(a => a!["id"]!.GetValue<Guid>() == accountId)!["contribution"]?.Deserialize<ContributionView>(WebJson);

    private static async Task<(decimal, decimal?)> GetAsync(HttpClient client, Guid accountId, int year, CancellationToken cancellationToken)
    {
        var view = (await client.GetContributionAsync(accountId, year, cancellationToken))!;

        return (view.ContributedPln, view.LimitPln);
    }
}
