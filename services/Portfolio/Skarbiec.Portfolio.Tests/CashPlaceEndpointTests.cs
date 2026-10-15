using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.UpdateAsset;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.CashPlacesApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

// The cash place slices and the transaction writes on a balance are under test, so they are called directly; the fixtures only arrange.
public sealed class CashPlaceEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    private static readonly DateOnly Oct2 = new(2026, 10, 2);

    private static readonly DateOnly Oct3 = new(2026, 10, 3);

    private static readonly DateOnly Oct4 = new(2026, 10, 4);

    [Fact]
    public async Task CreateDepositAndList()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        Factory.InstrumentQuoteLookupClient.WithRate("USD", 4.00m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var created = await client.PostAsJsonAsync(CashPlacesUri, NewCashPlaceBody("Sejf", "PLN", "USD"), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var place = (await created.Content.ReadFromJsonAsync<CashPlaceView>(cancellationToken))!;
        Assert.Equal("Sejf", place.Name);
        Assert.Equal(["PLN", "USD"], place.Balances.Select(b => b.Currency).Order().ToArray());
        await client.RecordTransactionAsync(place.AssetId("PLN"), TransactionType.Deposit, 2_000m, CashDay1, cancellationToken, unitPrice: 1m);
        await client.RecordTransactionAsync(place.AssetId("USD"), TransactionType.Deposit, 300m, CashDay1, cancellationToken, unitPrice: 1m);

        var listResponse = await client.GetAsync(CashPlacesUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = (await listResponse.Content.ReadFromJsonAsync<CashPlacesView>(cancellationToken))!;
        var sejf = Assert.Single(list.Places);
        Assert.False(sejf.IsArchived);
        var pln = Assert.Single(sejf.Balances, b => b.Currency == "PLN");
        var usd = Assert.Single(sejf.Balances, b => b.Currency == "USD");
        Assert.Equal((2_000m, 2_000m), (pln.Balance, pln.ValuePln));
        Assert.Equal((300m, 1_200m), (usd.Balance, usd.ValuePln));
        Assert.Equal(3_200m, sejf.ValuePln);
        Assert.Equal([("PLN", 2_000m), ("USD", 300m)], list.Totals.OrderBy(t => t.Currency).Select(t => (t.Currency, t.Balance)).ToArray());
        Assert.Equal(3_200m, list.TotalValuePln);
        Assert.False(list.FxUnavailable);
        var read = await client.GetCashPlaceAsync(place.Id, cancellationToken);
        Assert.Equal(3_200m, read.ValuePln);
    }

    [Fact]
    public async Task Duplicates_Return409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var place = await client.CreateCashPlaceAsync(cancellationToken, "Sejf", "PLN", "USD");

        var sameName = await client.PostAsJsonAsync(CashPlacesUri, NewCashPlaceBody("Sejf", "EUR"), cancellationToken);
        var sameCurrency = await client.PostAsJsonAsync(CashPlaceCurrenciesUri(place.Id), new { currency = "USD" }, cancellationToken);
        var newCurrency = await client.PostAsJsonAsync(CashPlaceCurrenciesUri(place.Id), new { currency = "EUR" }, cancellationToken);

        await sameName.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.DuplicateNameErrorCode, cancellationToken);
        await sameCurrency.AssertProblemAsync(HttpStatusCode.Conflict, DuplicateCurrencyErrorCode, cancellationToken);
        Assert.True(newCurrency.IsSuccessStatusCode);
        var list = await client.ListCashPlacesAsync(cancellationToken);
        Assert.Equal(["EUR", "PLN", "USD"], Assert.Single(list.Places).Balances.Select(b => b.Currency).Order().ToArray());
    }

    [Fact]
    public async Task FxUnavailable_FailsSoft()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        Factory.InstrumentQuoteLookupClient.WithUnavailable();
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var place = await client.CreateCashPlaceAsync(cancellationToken, "Sejf", "PLN", "USD");
        await client.RecordTransactionAsync(place.AssetId("PLN"), TransactionType.Deposit, 2_000m, CashDay1, cancellationToken, unitPrice: 1m);
        await client.RecordTransactionAsync(place.AssetId("USD"), TransactionType.Deposit, 300m, CashDay1, cancellationToken, unitPrice: 1m);

        var response = await client.GetAsync(CashPlacesUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = (await response.Content.ReadFromJsonAsync<CashPlacesView>(cancellationToken))!;
        var sejf = Assert.Single(list.Places);
        Assert.Equal(2_000m, Assert.Single(sejf.Balances, b => b.Currency == "PLN").ValuePln);
        var usd = Assert.Single(sejf.Balances, b => b.Currency == "USD");
        Assert.Equal(300m, usd.Balance);
        Assert.Null(usd.ValuePln);
        Assert.True(list.FxUnavailable);
    }

    [Fact]
    public async Task NegativeOnAnyDay_Returns400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var place = await client.CreateCashPlaceAsync(cancellationToken, "Sejf");
        var balanceId = place.AssetId("PLN");
        var depositId = await client.RecordTransactionAsync(balanceId, TransactionType.Deposit, 1_000m, Oct2, cancellationToken, unitPrice: 1m);
        await client.RecordTransactionAsync(balanceId, TransactionType.Withdraw, 800m, Oct4, cancellationToken, unitPrice: 1m);

        var withdraw = await client.PostCashTransactionAsync(balanceId, TransactionType.Withdraw, 500m, Oct3, cancellationToken);
        var deleteDeposit = await client.DeleteAsync(TransactionUri(balanceId, depositId), cancellationToken);

        await withdraw.AssertInsufficientFundsAsync(cancellationToken);
        await deleteDeposit.AssertInsufficientFundsAsync(cancellationToken);
        Assert.Equal(200m, await client.GetBalanceAsync(balanceId, cancellationToken));
        Assert.Equal(2, (await client.ListTransactionsAsync(balanceId, cancellationToken)).TotalCount);
    }

    [Fact]
    public async Task BalancesAreManaged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var place = await client.CreateFundedCashPlaceAsync(cancellationToken, 100m, CashDay1);
        var balanceId = place.AssetId("PLN");

        var update = await client.PutAsJsonAsync(
            AssetUri(balanceId),
            new UpdateAssetRequest { AssetClass = AssetClass.Cash, Name = "Renamed", Currency = "PLN" },
            cancellationToken);
        var remove = await client.DeleteAsync(AssetUri(balanceId), cancellationToken);
        var archive = await client.PostAsync(ArchiveAssetUri(balanceId), content: null, cancellationToken);
        var restore = await client.PostAsync(RestoreAssetUri(balanceId), content: null, cancellationToken);

        await update.AssertCashPlaceManagedAsync(cancellationToken);
        await remove.AssertCashPlaceManagedAsync(cancellationToken);
        await archive.AssertCashPlaceManagedAsync(cancellationToken);
        await restore.AssertCashPlaceManagedAsync(cancellationToken);
        var unchanged = await client.GetAssetAsync(balanceId, cancellationToken);
        Assert.Equal("Sejf", unchanged.Name);
        Assert.False(unchanged.IsArchived);
        Assert.Equal(100m, unchanged.Quantity);
    }

    [Fact]
    public async Task ArchiveAndDelete()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var sejf = await client.CreateFundedCashPlaceAsync(cancellationToken, 100m, CashDay1);
        var balanceId = sejf.AssetId("PLN");
        var skarbonka = await client.CreateCashPlaceAsync(cancellationToken, "Skarbonka", "PLN", "EUR");

        var archiveNotEmpty = await client.PostAsync(ArchiveCashPlaceUri(sejf.Id), content: null, cancellationToken);
        await client.RecordTransactionAsync(balanceId, TransactionType.Withdraw, 100m, CashDay2, cancellationToken, unitPrice: 1m);
        var archive = await client.PostAsync(ArchiveCashPlaceUri(sejf.Id), content: null, cancellationToken);
        var archiveAgain = await client.PostAsync(ArchiveCashPlaceUri(sejf.Id), content: null, cancellationToken);
        var depositWhenArchived = await client.PostCashTransactionAsync(
            balanceId, TransactionType.Deposit, 50m, CashDay3, cancellationToken);
        var restore = await client.PostAsync(RestoreCashPlaceUri(sejf.Id), content: null, cancellationToken);
        var deleteWithHistory = await client.DeleteAsync(CashPlaceUri(sejf.Id), cancellationToken);
        var deleteUntouched = await client.DeleteAsync(CashPlaceUri(skarbonka.Id), cancellationToken);

        await archiveNotEmpty.AssertProblemAsync(HttpStatusCode.Conflict, CashPlaceNotEmptyErrorCode, cancellationToken);
        Assert.True(archive.IsSuccessStatusCode);
        Assert.True(archiveAgain.IsSuccessStatusCode);
        await depositWhenArchived.AssertProblemAsync(HttpStatusCode.Conflict, CashPlaceArchivedErrorCode, cancellationToken);
        Assert.True(restore.IsSuccessStatusCode);
        await deleteWithHistory.AssertProblemAsync(HttpStatusCode.Conflict, CashPlaceHasHistoryErrorCode, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteUntouched.StatusCode);
        var places = (await client.ListCashPlacesAsync(cancellationToken)).Places;
        Assert.Equal(sejf.Id, Assert.Single(places).Id);
        await using var dbContext = CreateDbContext(userId);
        Assert.Equal(1, await dbContext.Assets.CountAsync(a => a.CashPlaceId != null, cancellationToken));
    }
}
