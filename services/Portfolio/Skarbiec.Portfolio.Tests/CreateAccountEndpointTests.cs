using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.Accounts.CreateAccount;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;

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

        Assert.Empty((await client.ListCashPlacesAsync(cancellationToken)).Places);
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

    [Fact]
    public async Task BankAccount_CreatedWithTerms()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);

        var mBank = await client.PostAsJsonAsync(
            AccountsUri, NewBankAccountBody("mBank", CurrentVariant, "PLN", "mBank", 0m), cancellationToken);
        var nest = await client.PostAsJsonAsync(
            AccountsUri, NewBankAccountBody("Nest Oszczędnościowe", SavingsVariant, "PLN", "Nest Bank", 5m), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, mBank.StatusCode);
        Assert.Equal(HttpStatusCode.Created, nest.StatusCode);
        var mBankBody = await mBank.ReadJsonAsync(cancellationToken);
        var nestBody = await nest.ReadJsonAsync(cancellationToken);
        Assert.Equal("PLN", mBankBody.GetProperty("currency").GetString());
        Assert.Equal(CurrentVariant, mBankBody.Bank().VariantName());
        Assert.Equal(0m, mBankBody.Bank().GetProperty("annualInterestRatePercent").GetDecimal());
        Assert.Equal(SavingsVariant, nestBody.Bank().VariantName());
        Assert.Equal(5m, nestBody.Bank().GetProperty("annualInterestRatePercent").GetDecimal());
        Assert.Equal("Nest Bank", nestBody.Bank().GetProperty("bankName").GetString());
        Assert.Equal((int)TaxWrapper.Regular, nestBody.GetProperty("wrapper").GetInt32());

        await using var dbContext = CreateDbContext(userId);
        var accountId = nestBody.GetProperty("id").GetGuid();
        var money = await dbContext.Assets.SingleAsync(a => a.AccountId == accountId, cancellationToken);
        Assert.Equal(nestBody.GetProperty("cashAssetId").GetGuid(), money.Id);
        Assert.Equal(AssetClass.BankAccount, money.AssetClass);
        Assert.Equal("PLN", money.Currency);
        Assert.Equal(AssetValuationMode.CurrencyValued, money.ValuationMode);
    }

    [Fact]
    public async Task BankFields_Rejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var ikeBank = await client.PostAsJsonAsync(
            AccountsUri, NewBankAccountBody(wrapper: TaxWrapper.Ike), cancellationToken);
        var usdBrokerage = await client.PostAsJsonAsync(
            AccountsUri,
            new { name = "XTB USD", type = (int)AccountType.Brokerage, wrapper = (int)TaxWrapper.Regular, currency = "USD" },
            cancellationToken);

        await ikeBank.AssertProblemAsync(HttpStatusCode.BadRequest, WrapperNotAllowedErrorCode, cancellationToken);
        await usdBrokerage.AssertProblemAsync(HttpStatusCode.BadRequest, BankFieldsNotAllowedErrorCode, cancellationToken);
    }
}
