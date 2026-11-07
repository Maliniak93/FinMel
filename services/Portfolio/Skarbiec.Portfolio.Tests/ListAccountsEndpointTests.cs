using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.Bonds;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class ListAccountsEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task NetContributions_CountsExternalFlowsOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var scenario = await client.CreateNetContributionsScenarioAsync(cancellationToken);

        var response = await client.GetAsync(AccountsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var accounts = (await response.ReadJsonAsync(cancellationToken)).EnumerateArray().ToDictionary(a => a.GetProperty("id").GetGuid());
        Assert.Multiple(
            () => Assert.Equal(7_300m, accounts[scenario.IkeId].GetProperty("netContributionsPln").GetDecimal()),
            () => Assert.Equal(8_500m, accounts[scenario.IkeId].GetProperty("contribution").GetProperty("contributedPln").GetDecimal()),
            () => Assert.Equal(1_000m, accounts[scenario.RegularId].GetProperty("netContributionsPln").GetDecimal()),
            () => Assert.Equal(300m, accounts[scenario.ArchivedId].GetProperty("netContributionsPln").GetDecimal()),
            () => Assert.Equal(JsonValueKind.Null, accounts[scenario.BankId].GetProperty("netContributionsPln").ValueKind),
            () => Assert.Equal(JsonValueKind.Null, accounts[scenario.BankId].GetProperty("positionsCostPln").ValueKind),
            () => Assert.Equal(JsonValueKind.Null, accounts[scenario.BankId].GetProperty("paidInterestPln").ValueKind));
    }

    [Fact]
    public async Task PositionsCost_SumsOpenStocksAndBonds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(new DateTimeOffset(2026, 6, 20, 10, 0, 0, TimeSpan.Zero));
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var edo = await client.CreateEdoYearOneSettledAsync(cancellationToken);
        var coi = await client.CreateFundedBondAsync(
            cancellationToken,
            request: NewBondRequest(
                name: "Coi", seriesCode: "COI0630", type: TreasuryBondType.Coi,
                purchaseDate: new DateOnly(2025, 6, 10), marginPercent: null),
            cashToppedUpOn: new DateOnly(2025, 6, 1),
            accountName: "Coi");
        await client.SettleBondInterestAsync(coi.Bond.AssetId, [(1, null)], cancellationToken);
        await client.RedeemBondEarlyAsync(
            coi.Bond.AssetId, new DateOnly(2026, 6, 15), 10, cancellationToken, runningPeriodRatePercent: 5.00m);
        var redeemed = await client.CreateFundedBondAsync(
            cancellationToken,
            request: NewBondRequest(
                name: "CoiDone", seriesCode: "COI0630", type: TreasuryBondType.Coi,
                purchaseDate: new DateOnly(2025, 6, 10), marginPercent: null),
            cashToppedUpOn: new DateOnly(2025, 6, 1),
            accountName: "CoiDone");
        await client.SettleBondInterestAsync(redeemed.Bond.AssetId, [(1, null)], cancellationToken);
        await client.RedeemBondEarlyAsync(
            redeemed.Bond.AssetId, new DateOnly(2026, 6, 15), 50, cancellationToken, runningPeriodRatePercent: 5.00m);
        var euroEtf = await client.CreateBrokerageHoldingAsync(
            cancellationToken, cashBalance: 10_000m, currency: "EUR", accountName: "Euro");
        await client.RecordBrokerageTradeAsync(
            euroEtf.AssetId, cancellationToken, quantity: 10m, unitPrice: 100m, date: CashDay1, amountPln: 4_312.50m);
        var etf = await client.CreateBrokerageHoldingAsync(cancellationToken, cashBalance: 10_000m, accountName: "Brokerage");
        await client.RecordBrokerageTradeAsync(etf.AssetId, cancellationToken, quantity: 10m, unitPrice: 120m, date: CashDay1);
        var archivedStock = await client.AddMarketAssetAsync(
            etf.AccountId, cancellationToken, AssetClass.Stock, "PLN", FakeInstrumentLookupClient.IdFor(AssetClass.Stock, "PLN"), "Archived");
        await client.RecordBrokerageTradeAsync(archivedStock, cancellationToken, quantity: 5m, unitPrice: 100m, date: CashDay1);
        await client.ArchiveAssetAsync(archivedStock, cancellationToken);
        var bonds = (await (await client.GetAsync(AllBondsUri, cancellationToken)).Content.ReadFromJsonAsync<List<BondResponse>>(cancellationToken))!
            .ToDictionary(b => b.AssetId);

        var response = await client.GetAsync(AccountsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var accounts = (await response.ReadJsonAsync(cancellationToken)).EnumerateArray().ToDictionary(a => a.GetProperty("id").GetGuid());
        var coiBond = bonds[coi.Bond.AssetId];
        var edoBond = bonds[edo.Bond.AssetId];
        Assert.Multiple(
            () => Assert.Equal(1_200m, accounts[etf.AccountId].GetProperty("positionsCostPln").GetDecimal()),
            () => Assert.Equal(0m, accounts[etf.AccountId].GetProperty("paidInterestPln").GetDecimal()),
            () => Assert.Equal(40 * coiBond.PurchasePricePerBond, accounts[coi.BondAccountId].GetProperty("positionsCostPln").GetDecimal()),
            () => Assert.True(coiBond.PaidInterestPln > 0m),
            () => Assert.Equal(coiBond.PaidInterestPln, accounts[coi.BondAccountId].GetProperty("paidInterestPln").GetDecimal()),
            () => Assert.Equal(edoBond.BondCount * edoBond.PurchasePricePerBond, accounts[edo.BondAccountId].GetProperty("positionsCostPln").GetDecimal()),
            () => Assert.Equal(0m, accounts[edo.BondAccountId].GetProperty("paidInterestPln").GetDecimal()),
            () => Assert.Equal(0, bonds[redeemed.Bond.AssetId].BondCount),
            () => Assert.True(bonds[redeemed.Bond.AssetId].PaidInterestPln > 0m),
            () => Assert.Equal(0m, accounts[redeemed.BondAccountId].GetProperty("positionsCostPln").GetDecimal()),
            () => Assert.Equal(0m, accounts[redeemed.BondAccountId].GetProperty("paidInterestPln").GetDecimal()),
            () => Assert.Equal(4_312.50m, accounts[euroEtf.AccountId].GetProperty("positionsCostPln").GetDecimal()));
    }

    [Fact]
    public async Task PositionsCost_NullWhenAnyCostMissing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var etf = await client.CreateBrokerageHoldingAsync(cancellationToken, cashBalance: 10_000m);
        await client.RecordBrokerageTradeAsync(etf.AssetId, cancellationToken, quantity: 10m, unitPrice: 120m, date: CashDay1);
        await using (var dbContext = CreateDbContext(userId))
        {
            var buy = await dbContext.Transactions.SingleAsync(t => t.AssetId == etf.AssetId, cancellationToken);
            buy.FxRateToPln = null;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var response = await client.GetAsync(AccountsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var account = Assert.Single((await response.ReadJsonAsync(cancellationToken)).EnumerateArray().ToList());
        Assert.Equal(JsonValueKind.Null, account.GetProperty("positionsCostPln").ValueKind);
    }

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
    public async Task OutsideNetWorth_OmitsCashBalancePln()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        Factory.InstrumentQuoteLookupClient.WithRate("EUR", 4.30m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var flaggedEur = await client.CreateBankAccountAsync(
            cancellationToken, "Konto EUR", currency: "EUR", balance: 100m, outsideNetWorth: true);

        var response = await client.GetAsync(AccountsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var account = Assert.Single((await response.ReadJsonAsync(cancellationToken)).EnumerateArray().ToList());
        Assert.Equal(flaggedEur.AccountId, account.GetProperty("id").GetGuid());
        Assert.True(account.GetProperty("outsideNetWorth").GetBoolean());
        Assert.False(account.TryGetProperty("cashBalancePln", out _));
        Assert.Empty(Factory.InstrumentQuoteLookupClient.RateCalls);
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
