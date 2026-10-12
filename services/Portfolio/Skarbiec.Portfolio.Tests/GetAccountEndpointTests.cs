using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

// The account read is under test, so GET is called directly; AccountsApi and PortfolioApi helpers only arrange.
public sealed class GetAccountEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
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
        await client.SetContributionLimitAsync(ike.AccountId, 2026, 26_000m, cancellationToken);

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
