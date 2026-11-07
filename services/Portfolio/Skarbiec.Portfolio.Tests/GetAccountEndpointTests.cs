using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts.CreateAccount;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.AssetGroupsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

// The account read is under test, so GET is called directly; AccountsApi and PortfolioApi helpers only arrange.
public sealed class GetAccountEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task OutsideNetWorth_OmitsCashBalancePln()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        Factory.InstrumentQuoteLookupClient.WithRate("EUR", 4.30m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var flagged = await client.CreateBankAccountAsync(
            cancellationToken, "Konto EUR", currency: "EUR", balance: 100m, outsideNetWorth: true);

        var response = await client.GetAsync(AccountUri(flagged.AccountId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadJsonAsync(cancellationToken);
        Assert.True(body.GetProperty("outsideNetWorth").GetBoolean());
        Assert.False(body.TryGetProperty("cashBalancePln", out _));
        Assert.Empty(Factory.InstrumentQuoteLookupClient.RateCalls);
    }

    [Fact]
    public async Task NetContributions_Returned()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var scenario = await client.CreateNetContributionsScenarioAsync(cancellationToken);

        var ike = await client.GetAsync(AccountUri(scenario.IkeId), cancellationToken);
        var bank = await client.GetAsync(AccountUri(scenario.BankId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, ike.StatusCode);
        var ikeBody = await ike.ReadJsonAsync(cancellationToken);
        Assert.Multiple(
            () => Assert.Equal(7_300m, ikeBody.GetProperty("netContributionsPln").GetDecimal()),
            () => Assert.Equal(1_200m, ikeBody.GetProperty("positionsCostPln").GetDecimal()),
            () => Assert.Equal(0m, ikeBody.GetProperty("paidInterestPln").GetDecimal()),
            () => Assert.Equal(8_500m, ikeBody.GetProperty("contribution").GetProperty("contributedPln").GetDecimal()));
        var bankBody = await bank.ReadJsonAsync(cancellationToken);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, bankBody.GetProperty("netContributionsPln").ValueKind);
    }

    [Fact]
    public async Task ReturnsGroupId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var group = await client.CreateAssetGroupAsync(cancellationToken);
        var brokerage = await client.PostAsJsonAsync(
            AccountsUri,
            new CreateAccountRequest { Name = "XTB", Type = AccountType.Brokerage, Wrapper = TaxWrapper.Regular, GroupId = group.Id },
            cancellationToken);
        var brokerageId = (await brokerage.ReadJsonAsync(cancellationToken)).GetProperty("id").GetGuid();
        var bank = await client.PostAsJsonAsync(AccountsUri, NewBankAccountBody("mBank", groupId: group.Id), cancellationToken);
        var bankId = (await bank.ReadJsonAsync(cancellationToken)).GetProperty("id").GetGuid();

        var storedBrokerage = await client.GetAsync(AccountUri(brokerageId), cancellationToken);
        var storedBank = await client.GetAsync(AccountUri(bankId), cancellationToken);

        Assert.Equal(group.Id, (await storedBrokerage.ReadJsonAsync(cancellationToken)).GetProperty("groupId").GetGuid());
        Assert.Equal(group.Id, (await storedBank.ReadJsonAsync(cancellationToken)).GetProperty("groupId").GetGuid());
    }

    [Fact]
    public async Task Contribution_CountsExternalAndIncomingOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var source = await client.CreateAccountWithCashAsync(
            cancellationToken, balance: 5_000m, name: "XTB zwykly", depositedOn: new DateOnly(2025, 12, 1));
        var ike = await client.CreateAccountWithCashAsync(cancellationToken, name: "XTB IKE", wrapper: TaxWrapper.Ike);
        await client.RecordTransactionAsync(
            ike.CashAssetId, TransactionType.Deposit, 3_000m, new DateOnly(2025, 12, 31), cancellationToken, unitPrice: 1m);
        await client.RecordTransactionAsync(
            ike.CashAssetId, TransactionType.Deposit, 10_000m, new DateOnly(2026, 1, 5), cancellationToken, unitPrice: 1m);
        await client.CreateTransferAsync(
            cancellationToken, NewTransferRequest(source.CashAssetId, ike.CashAssetId, 2_000m, new DateOnly(2026, 2, 1)));
        var stockId = await client.AddMarketAssetAsync(
            ike.AccountId, cancellationToken, AssetClass.Stock, "PLN", FakeInstrumentLookupClient.IdFor(AssetClass.Stock, "PLN"), "XTB stock");
        await client.RecordBrokerageTradeAsync(
            stockId, cancellationToken, TransactionType.Buy, quantity: 5m, unitPrice: 100m, date: new DateOnly(2026, 2, 2));
        await client.RecordBrokerageTradeAsync(
            stockId, cancellationToken, TransactionType.Dividend, quantity: 50m, unitPrice: 1m, date: new DateOnly(2026, 2, 15));
        await client.RecordBrokerageTradeAsync(
            stockId, cancellationToken, TransactionType.Sell, quantity: 5m, unitPrice: 100m, date: new DateOnly(2026, 3, 1));
        await client.RecordTransactionAsync(
            ike.CashAssetId, TransactionType.Withdraw, 1_000m, new DateOnly(2026, 3, 2), cancellationToken, unitPrice: 1m);
        await client.SetContributionLimitsAsync(2026, 26_000m, null, cancellationToken);

        var contribution2026 = await client.GetContributionAsync(ike.AccountId, 2026, cancellationToken);
        var contribution2025 = await client.GetContributionAsync(ike.AccountId, 2025, cancellationToken);

        Assert.Equal(new ContributionView(2026, 12_000m, 26_000m), contribution2026);
        Assert.Equal(new ContributionView(2025, 3_000m, null), contribution2025);
    }

    [Fact]
    public async Task Contribution_NullForRegular()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken, "XTB zwykly");

        var response = await client.GetAsync(AccountUri(accountId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var account = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
        Assert.True(account!.AsObject().ContainsKey("contribution"));
        Assert.Null(account["contribution"]);
    }
}
