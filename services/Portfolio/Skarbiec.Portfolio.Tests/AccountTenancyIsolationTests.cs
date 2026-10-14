using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.Accounts.UpdateAccount;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using Skarbiec.Testing.Tenancy;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;

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
    public async Task Cash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await owner.CreateAccountWithCashAsync(cancellationToken, balance: 300m);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var deposit = await stranger.PostCashTransactionAsync(
            account.CashAssetId, TransactionType.Deposit, 50m, CashDay2, cancellationToken);
        var withdraw = await stranger.PostCashTransactionAsync(
            account.CashAssetId, TransactionType.Withdraw, 50m, CashDay2, cancellationToken);
        var read = await stranger.GetAsync(PortfolioApi.AssetUri(account.CashAssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, deposit.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, withdraw.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(300m, await owner.GetCashBalanceAsync(account.AccountId, cancellationToken));
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

    [Fact]
    public async Task ContributionLimit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await owner.CreateAccountAsync(cancellationToken, "XTB IKE", wrapper: TaxWrapper.Ike);
        await owner.SetContributionLimitAsync(accountId, 2026, 26_000m, cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var set = await stranger.PutAsJsonAsync(ContributionLimitUri(accountId, 2026), new { amount = 1m }, cancellationToken);
        var delete = await stranger.DeleteAsync(ContributionLimitUri(accountId, 2026), cancellationToken);
        var read = await stranger.GetAsync(AccountContributionUri(accountId, 2026), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, set.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(26_000m, (await owner.GetContributionAsync(accountId, 2026, cancellationToken))!.LimitPln);
    }

    [Fact]
    public async Task BankInterest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BankInterestSeptemberEndedUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var nest = await owner.CreateBankAccountAsync(
            cancellationToken, "Nest Oszczędnościowe", SavingsVariant, annualInterestRatePercent: 5m, balance: 10_000m, depositedOn: AugustOpening);
        await owner.SettlePreviewedInterestAsync(nest.AccountId, cancellationToken);
        var settlementId = (await owner.GetAccountJsonAsync(nest.AccountId, cancellationToken))
            .Bank().GetProperty("lastSettlement").GetProperty("settlementId").GetGuid();

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var preview = await stranger.GetAsync(InterestPreviewUri(nest.AccountId), cancellationToken);
        var settle = await stranger.PostAsJsonAsync(
            InterestSettlementsUri(nest.AccountId), NewSettleInterestBody(AugustEnd, 1m, 0m), cancellationToken);
        var undo = await stranger.DeleteAsync(InterestSettlementUri(nest.AccountId, settlementId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, settle.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, undo.StatusCode);
        Assert.Equal(10_034.40m, await owner.GetCashBalanceAsync(nest.AccountId, cancellationToken));
    }
}
