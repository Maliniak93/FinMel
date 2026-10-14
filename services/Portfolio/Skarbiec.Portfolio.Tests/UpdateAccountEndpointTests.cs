using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.Accounts.UpdateAccount;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class UpdateAccountEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Update_Rename_ChangesOnlyTheName()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken, "XTB IKE", AccountType.Brokerage, TaxWrapper.Ike);

        var response = await client.PutAsJsonAsync(
            AccountUri(accountId), new UpdateAccountRequest { Name = "XTB IKE (renamed)" }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AccountResponse>(cancellationToken);
        Assert.Equal("XTB IKE (renamed)", body!.Name);
        Assert.Equal(AccountType.Brokerage, body.Type);
        Assert.Equal(TaxWrapper.Ike, body.Wrapper);
        var stored = await client.GetAccountAsync(accountId, cancellationToken);
        Assert.Equal("XTB IKE (renamed)", stored.Name);
        Assert.Equal(AccountType.Brokerage, stored.Type);
        Assert.Equal(TaxWrapper.Ike, stored.Wrapper);
    }

    [Fact]
    public async Task Update_ToNameTakenByAnotherOwnAccount_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateAccountAsync(cancellationToken, "Taken");
        var accountId = await client.CreateAccountAsync(cancellationToken, "Free");

        var response = await client.PutAsJsonAsync(
            AccountUri(accountId), new UpdateAccountRequest { Name = "Taken" }, cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.DuplicateNameErrorCode, cancellationToken);
    }

    [Fact]
    public async Task Update_NonExistentAccount_ReturnsNotFound()
    {
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PutAsJsonAsync(
            AccountUri(Guid.NewGuid()), new UpdateAccountRequest { Name = "Anything" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task BankTerms()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var mBank = await client.CreateBankAccountAsync(cancellationToken, balance: 1_000m);

        var response = await client.PutAsJsonAsync(
            AccountUri(mBank.AccountId),
            NewUpdateBankBody("mBank", SavingsVariant, "mBank S.A.", 3.5m),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var account = await client.GetAccountJsonAsync(mBank.AccountId, cancellationToken);
        Assert.Equal("mBank", account.GetProperty("name").GetString());
        Assert.Equal((int)AccountType.Bank, account.GetProperty("type").GetInt32());
        Assert.Equal((int)TaxWrapper.Regular, account.GetProperty("wrapper").GetInt32());
        Assert.Equal("PLN", account.GetProperty("currency").GetString());
        Assert.Equal(1_000m, account.GetProperty("cashBalance").GetDecimal());
        Assert.Equal(SavingsVariant, account.Bank().VariantName());
        Assert.Equal("mBank S.A.", account.Bank().GetProperty("bankName").GetString());
        Assert.Equal(3.5m, account.Bank().GetProperty("annualInterestRatePercent").GetDecimal());

        var archive = await client.PostAsync(ArchiveAccountUri(mBank.AccountId), content: null, cancellationToken);

        await archive.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.AccountNotEmptyErrorCode, cancellationToken);
    }
}
