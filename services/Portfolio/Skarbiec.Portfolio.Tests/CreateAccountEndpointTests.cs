using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.Accounts.CreateAccount;
using Skarbiec.Portfolio.Features.CashAccounts;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;

namespace Skarbiec.Portfolio.Tests;

// Creation is under test, so these facts post directly: AccountsApi.CreateAccountAsync asserts success.
public sealed class CreateAccountEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Create_WithValidRequest_ReturnsCreatedWithTypeAndWrapper()
    {
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var request = new CreateAccountRequest { Name = "XTB IKE", Type = AccountType.Brokerage, Wrapper = TaxWrapper.Ike };

        var response = await client.PostAsJsonAsync(AccountsUri, request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AccountResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal("XTB IKE", body.Name);
        Assert.Equal(AccountType.Brokerage, body.Type);
        Assert.Equal(TaxWrapper.Ike, body.Wrapper);
        Assert.False(body.IsArchived);
        Assert.Equal(AccountUri(body.Id), response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task CreatesAccountCash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);

        var response = await client.PostAsJsonAsync(
            AccountsUri,
            new CreateAccountRequest { Name = "XTB IKE", Type = AccountType.Brokerage, Wrapper = TaxWrapper.Ike },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<AccountResponse>(cancellationToken);
        Assert.NotEqual(Guid.Empty, created!.CashAssetId);
        Assert.Equal(0m, created.CashBalance);

        var account = await client.GetAccountAsync(created.Id, cancellationToken);
        Assert.Equal(created.CashAssetId, account.CashAssetId);
        Assert.Equal(0m, account.CashBalance);

        await using var dbContext = CreateDbContext(userId);
        var cash = await dbContext.Assets.Where(a => a.AccountId == created.Id && a.AssetClass == AssetClass.Cash).ToListAsync(cancellationToken);
        var asset = Assert.Single(cash);
        Assert.Equal(created.CashAssetId, asset.Id);
        Assert.Equal("PLN", asset.Currency);
        Assert.Equal(AssetValuationMode.CurrencyValued, asset.ValuationMode);
        Assert.Equal("XTB IKE", asset.Name);
        Assert.Equal(0m, asset.Quantity);

        var freeCash = await client.GetFromJsonAsync<CashAccountsResponse>(PortfolioApi.AllCashAccountsUri, cancellationToken);
        Assert.Empty(freeCash!.Accounts);
    }

    [Fact]
    public async Task Create_WithDuplicateNameForSameUser_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateAccountAsync(cancellationToken, "XTB IKE", AccountType.Brokerage, TaxWrapper.Ike);

        var response = await client.PostAsJsonAsync(
            AccountsUri,
            new CreateAccountRequest { Name = "XTB IKE", Type = AccountType.Bonds, Wrapper = TaxWrapper.Regular },
            cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.DuplicateNameErrorCode, cancellationToken);
    }

    [Fact]
    public async Task Create_SameNameForDifferentUsers_BothSucceed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var clientA = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        using var clientB = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var request = new CreateAccountRequest { Name = "XTB IKE", Type = AccountType.Brokerage, Wrapper = TaxWrapper.Ike };

        var responseA = await clientA.PostAsJsonAsync(AccountsUri, request, cancellationToken);
        var responseB = await clientB.PostAsJsonAsync(AccountsUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, responseA.StatusCode);
        Assert.Equal(HttpStatusCode.Created, responseB.StatusCode);
    }

    [Fact]
    public async Task Create_WithEmptyName_ReturnsBadRequestWithFieldDetails()
    {
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var request = new CreateAccountRequest { Name = "", Type = AccountType.Brokerage, Wrapper = TaxWrapper.Regular };

        var response = await client.PostAsJsonAsync(AccountsUri, request, TestContext.Current.CancellationToken);

        await response.AssertFieldValidationErrorAsync(nameof(CreateAccountRequest.Name), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Create_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            AccountsUri,
            new CreateAccountRequest { Name = "XTB", Type = AccountType.Brokerage, Wrapper = TaxWrapper.Regular },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
