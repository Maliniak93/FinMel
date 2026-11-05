using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class ListAccountsEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task List_ReturnsEveryAccountByNameArchivedIncluded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateAccountAsync(cancellationToken, "XTB zwykly");
        var bondsId = await client.CreateAccountAsync(cancellationToken, "Obligacje IKE", AccountType.Bonds, TaxWrapper.Ike);
        await client.CreateAccountAsync(cancellationToken, "Archiwum");
        await client.ArchiveAccountAsync(bondsId, cancellationToken);

        var response = await client.GetAsync(AccountsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var accounts = await response.Content.ReadFromJsonAsync<List<AccountResponse>>(cancellationToken);
        Assert.Equal(["Archiwum", "Obligacje IKE", "XTB zwykly"], accounts!.Select(a => a.Name));
        var bonds = Assert.Single(accounts!, a => a.Id == bondsId);
        Assert.True(bonds.IsArchived);
        Assert.Equal(AccountType.Bonds, bonds.Type);
        Assert.Equal(TaxWrapper.Ike, bonds.Wrapper);
    }

    [Fact]
    public async Task OutsideNetWorth_ValuesInPln()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        Factory.InstrumentQuoteLookupClient.WithRate("EUR", 4.30m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var flaggedEur = await client.CreateBankAccountAsync(
            cancellationToken, "Konto EUR", currency: "EUR", balance: 100m, outsideNetWorth: true);
        var flaggedPln = await client.CreateBankAccountAsync(
            cancellationToken, "Konto PLN", balance: 250m, outsideNetWorth: true);
        var unflaggedEur = await client.CreateBankAccountAsync(cancellationToken, "Zwykle EUR", currency: "EUR", balance: 50m);

        var response = await client.GetAsync(AccountsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var accounts = (await response.ReadJsonAsync(cancellationToken)).EnumerateArray().ToList();
        var eur = accounts.Single(a => a.GetProperty("id").GetGuid() == flaggedEur.AccountId);
        Assert.True(eur.GetProperty("outsideNetWorth").GetBoolean());
        Assert.Equal(430m, eur.GetProperty("cashBalancePln").GetDecimal());
        var pln = accounts.Single(a => a.GetProperty("id").GetGuid() == flaggedPln.AccountId);
        Assert.Equal(250m, pln.GetProperty("cashBalancePln").GetDecimal());
        var plain = accounts.Single(a => a.GetProperty("id").GetGuid() == unflaggedEur.AccountId);
        Assert.False(plain.GetProperty("outsideNetWorth").GetBoolean());
        Assert.Equal(JsonValueKind.Null, plain.GetProperty("cashBalancePln").ValueKind);
    }

    [Fact]
    public async Task OutsideNetWorth_FxUnavailable_ReturnsNull()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        Factory.InstrumentQuoteLookupClient.WithUnavailable();
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var flaggedEur = await client.CreateBankAccountAsync(
            cancellationToken, "Konto EUR", currency: "EUR", balance: 100m, outsideNetWorth: true);

        var response = await client.GetAsync(AccountsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var account = Assert.Single((await response.ReadJsonAsync(cancellationToken)).EnumerateArray().ToList());
        Assert.Equal(flaggedEur.AccountId, account.GetProperty("id").GetGuid());
        Assert.True(account.GetProperty("outsideNetWorth").GetBoolean());
        Assert.Equal(JsonValueKind.Null, account.GetProperty("cashBalancePln").ValueKind);
    }

    [Fact]
    public async Task FilterByType()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateAccountAsync(cancellationToken, "XTB");
        var mBank = await client.CreateBankAccountAsync(cancellationToken, "mBank");
        var nest = await client.CreateBankAccountAsync(cancellationToken, "Nest Oszczędnościowe", SavingsVariant, annualInterestRatePercent: 5m);

        var response = await client.GetAsync(AccountsOfTypeUri(AccountType.Bank), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var accounts = await response.Content.ReadFromJsonAsync<List<AccountResponse>>(cancellationToken);
        Assert.Equal(new[] { mBank.AccountId, nest.AccountId }.Order(), accounts!.Select(a => a.Id).Order());
        Assert.All(accounts!, a => Assert.Equal(AccountType.Bank, a.Type));
    }
}
