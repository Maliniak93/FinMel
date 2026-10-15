using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using Skarbiec.Testing.Tenancy;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.CashPlacesApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class CashPlaceTenancyIsolationTests(SkarbiecContainersFixture containers) : TenancyIsolationTests<Program>
{
    protected override PortfolioApiFactory Factory { get; } = new(containers);

    protected override async Task<Uri> CreateResourceAsync(HttpClient ownerClient, CancellationToken cancellationToken)
    {
        var place = await ownerClient.CreateCashPlaceAsync(cancellationToken);

        return new Uri(CashPlaceUri(place.Id), UriKind.Relative);
    }

    protected override Uri ListUrl { get; } = new(CashPlacesUri, UriKind.Relative);

    protected override HttpContent CreateUpdatePayload() => JsonContent.Create(new { name = "Stranger edit" });

    protected override async Task AssertResourceAbsentFromListAsync(HttpResponseMessage listResponse, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<CashPlacesView>(cancellationToken);

        Assert.Empty(list!.Places);
        Assert.Empty(list.Totals);
    }

    [Fact]
    public async Task Rename_ArchiveRestoreAndDelete_ByStranger_ReturnNotFoundAndLeavePlaceUnchanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var place = await owner.CreateCashPlaceAsync(cancellationToken, "Mine");

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var rename = await stranger.PutAsJsonAsync(CashPlaceUri(place.Id), new { name = "Stranger edit" }, cancellationToken);
        var archive = await stranger.PostAsync(ArchiveCashPlaceUri(place.Id), content: null, cancellationToken);
        var restore = await stranger.PostAsync(RestoreCashPlaceUri(place.Id), content: null, cancellationToken);
        var addCurrency = await stranger.PostAsJsonAsync(CashPlaceCurrenciesUri(place.Id), new { currency = "USD" }, cancellationToken);
        var delete = await stranger.DeleteAsync(CashPlaceUri(place.Id), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, rename.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, archive.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, restore.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, addCurrency.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        var unchanged = await owner.GetCashPlaceAsync(place.Id, cancellationToken);
        Assert.Equal("Mine", unchanged.Name);
        Assert.False(unchanged.IsArchived);
        Assert.Equal(["PLN"], unchanged.Balances.Select(b => b.Currency).ToArray());
    }

    [Fact]
    public async Task Deposit_IntoStrangersBalance_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var place = await owner.CreateFundedCashPlaceAsync(cancellationToken, 300m, CashDay1);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var deposit = await stranger.PostCashTransactionAsync(place.AssetId(), TransactionType.Deposit, 50m, CashDay2, cancellationToken);
        var withdraw = await stranger.PostCashTransactionAsync(place.AssetId(), TransactionType.Withdraw, 50m, CashDay2, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, deposit.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, withdraw.StatusCode);
        Assert.Equal(300m, await owner.GetBalanceAsync(place.AssetId(), cancellationToken));
    }

    [Fact]
    public async Task Transfer_BetweenOwnersPlaceAndStrangersBank_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var place = await owner.CreateFundedCashPlaceAsync(cancellationToken, 300m, DefaultTopUpDate);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangersBank = await stranger.CreateBankAccountAsync(cancellationToken, balance: 1_000m, depositedOn: DefaultTopUpDate);

        var atm = await stranger.PostAsJsonAsync(
            TransfersUri, NewTransferRequest(strangersBank.MoneyAssetId, place.AssetId(), 100m, CashDay1), cancellationToken);
        var bankDeposit = await stranger.PostAsJsonAsync(
            TransfersUri, NewTransferRequest(place.AssetId(), strangersBank.MoneyAssetId, 100m, CashDay1), cancellationToken);

        // A stranger's source is a 404, a stranger's target the same 400 as any unsuitable counterpart: neither reveals the place.
        await atm.AssertInvalidTransferCounterpartAsync(cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, bankDeposit.StatusCode);
        Assert.Equal(300m, await owner.GetBalanceAsync(place.AssetId(), cancellationToken));
        Assert.Equal(1_000m, await stranger.GetBalanceAsync(strangersBank.MoneyAssetId, cancellationToken));
    }

    [Fact]
    public async Task TransferCandidates_ForStranger_DoNotIncludeOwnersBalances()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await owner.CreateCashPlaceAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await stranger.GetAsync(CashPlaceCandidatesUri("PLN"), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.ReadJsonAsync(cancellationToken)).EnumerateArray());
    }
}
