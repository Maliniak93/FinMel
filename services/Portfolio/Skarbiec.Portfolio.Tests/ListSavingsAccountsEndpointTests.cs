using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.SavingsAccounts;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class ListSavingsAccountsEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task List_ReturnsAccountsByNameWithBalance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountB = await client.AddSavingsAccountAsync(
            cancellationToken, NewSavingsAccountRequest(name: "B account"));
        var accountA = await client.AddSavingsAccountAsync(cancellationToken, NewSavingsAccountRequest(name: "A account"));
        await client.AddCashAssetAsync(cancellationToken);
        var oldAccount = await client.AddSavingsAccountAsync(
            cancellationToken, NewSavingsAccountRequest(name: "Old account", taxExempt: true));
        await client.RecordTransactionAsync(oldAccount.AssetId, TransactionType.Deposit, 500m, SavingsToday, cancellationToken, unitPrice: 1m);
        await client.RecordTransactionAsync(oldAccount.AssetId, TransactionType.Withdraw, 200m, SavingsToday, cancellationToken, unitPrice: 1m);

        var response = await client.GetAsync(AllSavingsAccountsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var listed = (await response.Content.ReadFromJsonAsync<List<SavingsAccountResponse>>(cancellationToken))!;
        Assert.Equal(
            [accountA.AssetId, accountB.AssetId, oldAccount.AssetId],
            listed.Select(a => a.AssetId).ToArray());
        var old = listed[2];
        Assert.Equal("Old account", old.Name);
        Assert.Equal(10_300m, old.Balance);
        Assert.True(old.TaxExempt);
        Assert.Equal(5.25m, old.AnnualInterestRatePercent);
        Assert.Equal(10_000m, listed[0].Balance);
    }

    [Fact]
    public async Task List_ArchivedAccount_ListedWithFlag()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var live = await client.AddSavingsAccountAsync(cancellationToken, NewSavingsAccountRequest(name: "Live account"));
        var archived = await client.AddSavingsAccountAsync(cancellationToken, NewSavingsAccountRequest(name: "Archived account"));
        await client.ArchiveAssetAsync(archived.AssetId, cancellationToken);

        var response = await client.GetAsync(AllSavingsAccountsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var listed = (await response.Content.ReadFromJsonAsync<List<SavingsAccountResponse>>(cancellationToken))!;
        Assert.Equal(2, listed.Count);
        Assert.False(listed.Single(a => a.AssetId == live.AssetId).IsArchived);
        Assert.True(listed.Single(a => a.AssetId == archived.AssetId).IsArchived);
    }

    [Fact]
    public async Task List_NoSavingsAccounts_ReturnsEmpty()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.AddCashAssetAsync(cancellationToken);
        await client.AddDepositAsync(cancellationToken);

        var response = await client.GetAsync(AllSavingsAccountsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<List<SavingsAccountResponse>>(cancellationToken))!);
    }

    [Fact]
    public async Task List_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync(AllSavingsAccountsUri, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_ReturnsInterestStatusAndLastSettlement()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(OctoberEndedUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var settledOnce = await client.AddSavingsAccountAsync(
            cancellationToken, NewInterestAccountRequest(name: "Settled once"));
        var untouched = await client.AddSavingsAccountAsync(cancellationToken, NewInterestAccountRequest(name: "Untouched"));
        var fresh = await client.AddSavingsAccountAsync(cancellationToken, NewInterestAccountRequest(name: "Fresh", openingDate: new DateOnly(2026, 11, 1)));
        await client.SettlePreviewedSavingsInterestAsync(settledOnce.AssetId, cancellationToken);

        var response = await client.GetAsync(AllSavingsAccountsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var accounts = (await response.Content.ReadFromJsonAsync<List<SavingsAccountResponse>>(cancellationToken))!;

        var once = accounts.Single(a => a.AssetId == settledOnce.AssetId);
        Assert.True(once.InterestDue);
        Assert.Equal(1, once.DuePeriodCount);
        Assert.NotNull(once.LastSettlement);
        Assert.NotEqual(Guid.Empty, once.LastSettlement.SettlementId);
        Assert.Equal(new DateOnly(2026, 9, 1), once.LastSettlement.PeriodStart);
        Assert.Equal(new DateOnly(2026, 9, 30), once.LastSettlement.PeriodEnd);
        Assert.Equal(41.10m, once.LastSettlement.GrossInterest);
        Assert.Equal(7.81m, once.LastSettlement.Tax);
        Assert.Equal(33.29m, once.LastSettlement.NetInterest);

        var never = accounts.Single(a => a.AssetId == untouched.AssetId);
        Assert.True(never.InterestDue);
        Assert.Equal(2, never.DuePeriodCount);
        Assert.Null(never.LastSettlement);

        var opened = accounts.Single(a => a.AssetId == fresh.AssetId);
        Assert.False(opened.InterestDue);
        Assert.Equal(0, opened.DuePeriodCount);
        Assert.Null(opened.LastSettlement);
    }

    [Fact]
    public async Task Get_ReturnsInterestStatus()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.AddSavingsAccountAsync(cancellationToken, NewInterestAccountRequest());

        var due = await client.GetSavingsAccountAsync(account.AssetId, cancellationToken);
        Assert.True(due.InterestDue);
        Assert.Equal(1, due.DuePeriodCount);
        Assert.Null(due.LastSettlement);

        await client.SettlePreviewedSavingsInterestAsync(account.AssetId, cancellationToken);

        var settled = await client.GetSavingsAccountAsync(account.AssetId, cancellationToken);
        Assert.False(settled.InterestDue);
        Assert.Equal(0, settled.DuePeriodCount);
        Assert.Equal(33.29m, settled.LastSettlement!.NetInterest);
    }
}
