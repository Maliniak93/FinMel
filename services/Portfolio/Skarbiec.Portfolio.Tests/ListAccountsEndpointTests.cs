using System.Net;
using System.Net.Http.Json;
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
