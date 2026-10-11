using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Bonds;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class ListBondsEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task List_AcrossAccounts_OrdersByMaturityDateThenName()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var firstId = await client.CreateAccountAsync(cancellationToken, name: "First", type: AccountType.Bonds);
        var secondId = await client.CreateAccountAsync(cancellationToken, name: "Second", type: AccountType.Bonds);
        var september = new DateOnly(2026, 9, 1);
        await client.AddBondAsync(firstId, cancellationToken, NewBondRequest(name: "Zeta", seriesCode: "OTS1226", type: TreasuryBondType.Ots, purchaseDate: september));
        await client.AddBondAsync(secondId, cancellationToken, NewBondRequest(name: "Edo long"));
        await client.AddBondAsync(secondId, cancellationToken, NewBondRequest(name: "Alpha", seriesCode: "OTS1226", type: TreasuryBondType.Ots, purchaseDate: september));
        await client.AddBondAsync(firstId, cancellationToken, NewBondRequest(name: "Ror", seriesCode: "ROR0627", type: TreasuryBondType.Ror, purchaseDate: new DateOnly(2026, 6, 10)));

        var response = await client.GetAsync(AllBondsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bonds = await response.Content.ReadFromJsonAsync<List<BondResponse>>(cancellationToken);
        Assert.NotNull(bonds);
        Assert.Equal(["Alpha", "Zeta", "Ror", "Edo long"], bonds.Select(b => b.Name));
        Assert.Equal(secondId, bonds[0].AccountId);
        Assert.Equal("Second", bonds[0].AccountName);
        Assert.Equal(firstId, bonds[1].AccountId);
        Assert.Equal("First", bonds[1].AccountName);
        Assert.Equal(new DateOnly(2026, 12, 1), bonds[0].MaturityDate);
    }

    [Fact]
    public async Task ReturnsAccount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var ikeId = await client.CreateAccountAsync(cancellationToken, "Obligacje IKE", AccountType.Bonds, TaxWrapper.Ike);
        var bond = await client.AddBondAsync(ikeId, cancellationToken, NewBondRequest(name: "Edo IKE"));

        var response = await client.GetAsync(AllBondsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bonds = await response.Content.ReadFromJsonAsync<List<BondResponse>>(cancellationToken);
        var listed = Assert.Single(bonds!);
        Assert.Equal(bond.AssetId, listed.AssetId);
        Assert.Equal(ikeId, listed.AccountId);
        Assert.Equal("Obligacje IKE", listed.AccountName);
        Assert.Equal(TaxWrapper.Ike, listed.TaxWrapper);
    }

    [Fact]
    public async Task ReturnsAccount_SkipsArchivedAccount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterTosMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var closed = await client.CreateSettledTosAsync(cancellationToken);
        await client.RedeemBondAsync(closed.Bond.AssetId, closed.CashAssetId, cancellationToken);
        await client.ArchiveAccountAsync(closed.BondAccountId, cancellationToken);
        var liveAccountId = await client.CreateAccountAsync(cancellationToken, name: "Live", type: AccountType.Bonds);
        var live = await client.AddBondAsync(liveAccountId, cancellationToken, NewTosBondRequest() with { Name = "Tos live" });

        var response = await client.GetAsync(AllBondsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bonds = await response.Content.ReadFromJsonAsync<List<BondResponse>>(cancellationToken);
        var listed = Assert.Single(bonds!);
        Assert.Equal(live.AssetId, listed.AssetId);
        Assert.Equal(liveAccountId, listed.AccountId);
        Assert.Equal("Live", listed.AccountName);
    }

    [Fact]
    public async Task List_EstimatesWithOneBatchCall()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(EdoEarlyRedemptionDayUtc);
        Factory.BondRateLookupClient.WithRates("EDO1036", (2, 4.00m));
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken, type: AccountType.Bonds);
        await client.AddBondAsync(accountId, cancellationToken, NewEdoEarlyBondRequest(bondCount: 10) with { Name = "Edo A" });
        await client.AddBondAsync(accountId, cancellationToken, NewEdoEarlyBondRequest(bondCount: 5) with { Name = "Edo B" });
        await client.AddBondAsync(
            accountId, cancellationToken, NewTosBondRequest() with { Name = "Tos", PurchaseDate = new DateOnly(2026, 1, 15) });

        var response = await client.GetAsync(AllBondsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bonds = (await response.Content.ReadFromJsonAsync<List<BondResponse>>(cancellationToken))!.ToDictionary(b => b.Name);
        Assert.Equal(new[] { "EDO1036" }, Assert.Single(Factory.BondRateLookupClient.Calls));
        Assert.All(bonds.Values, b => Assert.Null(b.EstimateUnavailableReason));
        Assert.Equal(1_061.90m, bonds["Edo A"].Estimate!.GrossValue);
        Assert.Equal(530.95m, bonds["Edo B"].Estimate!.GrossValue);
        Assert.Equal(1_014.20m, bonds["Tos"].Estimate!.GrossValue);
        Assert.Equal(new DateOnly(2026, 5, 13), bonds["Edo A"].Estimate!.AsOf);
    }

    [Fact]
    public async Task List_MarketDataDown_EstimateNull()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(EdoEarlyRedemptionDayUtc);
        Factory.BondRateLookupClient.WithUnavailable();
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateAccountWithBondAsync(cancellationToken, NewEdoEarlyBondRequest());

        var response = await client.GetAsync(AllBondsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bond = Assert.Single((await response.Content.ReadFromJsonAsync<List<BondResponse>>(cancellationToken))!);
        Assert.Null(bond.Estimate);
        Assert.Equal(BondEstimateUnavailableReason.MarketDataUnavailable, bond.EstimateUnavailableReason);
    }
}
