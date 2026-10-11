using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.Accounts.UpdateAccount;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using Skarbiec.Testing.Tenancy;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class AccountTenancyIsolationTests(SkarbiecContainersFixture containers) : TenancyIsolationTests<Program>
{
    protected override PortfolioApiFactory Factory { get; } = new(containers);

    protected override async Task<Uri> CreateResourceAsync(HttpClient ownerClient, CancellationToken cancellationToken)
    {
        var accountId = await ownerClient.CreateAccountAsync(cancellationToken);

        return new Uri(AccountUri(accountId), UriKind.Relative);
    }

    protected override Uri ListUrl { get; } = new(AccountsUri, UriKind.Relative);

    protected override HttpContent CreateUpdatePayload() =>
        JsonContent.Create(new UpdateAccountRequest { Name = "Stranger edit" });

    protected override async Task AssertResourceAbsentFromListAsync(HttpResponseMessage listResponse, CancellationToken cancellationToken)
    {
        var accounts = await listResponse.Content.ReadFromJsonAsync<List<AccountResponse>>(cancellationToken);

        Assert.Empty(accounts!);
    }

    [Fact]
    public async Task Archive_ByStranger_ReturnsNotFoundAndLeavesAccountUnchanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await owner.CreateAccountAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await stranger.PostAsync(ArchiveAccountUri(accountId), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False((await owner.GetAccountAsync(accountId, cancellationToken)).IsArchived);
    }

    [Fact]
    public async Task Restore_ByStranger_ReturnsNotFoundAndLeavesAccountUnchanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await owner.CreateAccountAsync(cancellationToken);
        await owner.ArchiveAccountAsync(accountId, cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await stranger.PostAsync(RestoreAccountUri(accountId), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True((await owner.GetAccountAsync(accountId, cancellationToken)).IsArchived);
    }

    [Fact]
    public async Task Put_ByStranger_LeavesNameUnchanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await owner.CreateAccountAsync(cancellationToken, "Mine");

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await stranger.PutAsync(AccountUri(accountId), CreateUpdatePayload(), cancellationToken);

        Assert.Equal("Mine", (await owner.GetAccountAsync(accountId, cancellationToken)).Name);
    }

    [Fact]
    public async Task Delete_ByStranger_LeavesAccountInPlace()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await owner.CreateAccountAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await stranger.DeleteAsync(AccountUri(accountId), cancellationToken);

        Assert.Equal(accountId, (await owner.GetAccountAsync(accountId, cancellationToken)).Id);
    }

    [Fact]
    public async Task AddAsset_IntoStrangersAccount_ReturnsNotFoundAndAddsNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await owner.CreateAccountAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await stranger.PostAsJsonAsync(
            PortfolioApi.AllAssetsUri,
            new AddAssetRequest
            {
                AccountId = accountId,
                AssetClass = AssetClass.Stock,
                Name = "Intruder",
                Currency = "PLN",
                InstrumentId = FakeInstrumentLookupClient.IdFor(AssetClass.Stock, "PLN")
            },
            cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var assets = await owner.GetFromJsonAsync<List<AssetResponse>>(PortfolioApi.AssetsUri(accountId), cancellationToken);
        Assert.Empty(assets!);
    }
}
