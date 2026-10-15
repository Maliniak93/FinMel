using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Transfers.CreateTransfer;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.CashPlacesApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

// Transfer create and delete are under test, so they are posted directly; the fixtures only arrange.
public sealed class CashPlaceTransferEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    private static readonly DateOnly AtmDay = new(2026, 10, 5);

    [Fact]
    public async Task AtmWithdrawal_MovesBankMoneyIntoThePlaceAsTwoLinkedLegs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var mBank = await client.CreateBankAccountAsync(cancellationToken, balance: 5_000m, depositedOn: DefaultTopUpDate);
        var sejf = await client.CreateCashPlaceAsync(cancellationToken, "Sejf");

        var response = await client.PostAsJsonAsync(
            TransfersUri, NewTransferRequest(mBank.MoneyAssetId, sejf.AssetId(), 500m, AtmDay), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(4_500m, await client.GetBalanceAsync(mBank.MoneyAssetId, cancellationToken));
        Assert.Equal(500m, await client.GetBalanceAsync(sejf.AssetId(), cancellationToken));
        var transferId = (await response.Content.ReadFromJsonAsync<CreateTransferResponse>(cancellationToken))!.TransferId;
        await using var dbContext = CreateDbContext(userId);
        var legs = await dbContext.Transactions.Where(t => t.TransferId == transferId).ToListAsync(cancellationToken);
        Assert.Equal(2, legs.Count);
        Assert.Contains(legs, t => t.AssetId == mBank.MoneyAssetId && t.Type == TransactionType.Withdraw && t.Quantity == 500m && t.Date == AtmDay);
        Assert.Contains(legs, t => t.AssetId == sejf.AssetId() && t.Type == TransactionType.Deposit && t.Quantity == 500m && t.Date == AtmDay);
    }

    [Fact]
    public async Task BankDeposit_MovesPlaceMoneyIntoTheBankAndCanBeDeleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var mBank = await client.CreateBankAccountAsync(cancellationToken, balance: 5_000m, depositedOn: DefaultTopUpDate);
        var sejf = await client.CreateFundedCashPlaceAsync(cancellationToken, 1_000m, DefaultTopUpDate);

        var created = await client.PostAsJsonAsync(
            TransfersUri, NewTransferRequest(sejf.AssetId(), mBank.MoneyAssetId, 200m, AtmDay), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(800m, await client.GetBalanceAsync(sejf.AssetId(), cancellationToken));
        Assert.Equal(5_200m, await client.GetBalanceAsync(mBank.MoneyAssetId, cancellationToken));
        var transferId = (await created.Content.ReadFromJsonAsync<CreateTransferResponse>(cancellationToken))!.TransferId;

        var deleted = await client.DeleteAsync(TransferUri(transferId), cancellationToken);

        Assert.True(deleted.IsSuccessStatusCode);
        Assert.Equal(1_000m, await client.GetBalanceAsync(sejf.AssetId(), cancellationToken));
        Assert.Equal(5_000m, await client.GetBalanceAsync(mBank.MoneyAssetId, cancellationToken));
    }

    [Fact]
    public async Task BankDeposit_MoreThanThePlaceHolds_ReturnsInsufficientFunds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var mBank = await client.CreateBankAccountAsync(cancellationToken, balance: 5_000m, depositedOn: DefaultTopUpDate);
        var sejf = await client.CreateFundedCashPlaceAsync(cancellationToken, 100m, DefaultTopUpDate);

        var response = await client.PostAsJsonAsync(
            TransfersUri, NewTransferRequest(sejf.AssetId(), mBank.MoneyAssetId, 200m, AtmDay), cancellationToken);

        await response.AssertInsufficientFundsAsync(cancellationToken);
        Assert.Equal(100m, await client.GetBalanceAsync(sejf.AssetId(), cancellationToken));
        Assert.Equal(5_000m, await client.GetBalanceAsync(mBank.MoneyAssetId, cancellationToken));
    }

    [Fact]
    public async Task Create_InAnotherCurrency_ReturnsCurrencyMismatch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var mBankUsd = await client.CreateBankAccountAsync(
            cancellationToken, "mBank USD", currency: "USD", balance: 1_000m, depositedOn: DefaultTopUpDate);
        var sejf = await client.CreateCashPlaceAsync(cancellationToken, "Sejf");

        var response = await client.PostAsJsonAsync(
            TransfersUri, NewTransferRequest(mBankUsd.MoneyAssetId, sejf.AssetId(), 100m, AtmDay), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, TransferCurrencyMismatchErrorCode, cancellationToken);
        Assert.Equal(1_000m, await client.GetBalanceAsync(mBankUsd.MoneyAssetId, cancellationToken));
        Assert.Equal(0m, await client.GetBalanceAsync(sejf.AssetId(), cancellationToken));
    }

    [Fact]
    public async Task Create_ToAnotherPlaceOrToBrokerageCash_ReturnsInvalidCounterpart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var sejf = await client.CreateFundedCashPlaceAsync(cancellationToken, 1_000m, DefaultTopUpDate);
        var skarbonka = await client.CreateCashPlaceAsync(cancellationToken, "Skarbonka");
        var xtbIke = await client.CreateAccountWithCashAsync(
            cancellationToken, balance: 1_000m, name: "XTB IKE", depositedOn: DefaultTopUpDate, wrapper: TaxWrapper.Ike);

        var toPlace = await client.PostAsJsonAsync(
            TransfersUri, NewTransferRequest(sejf.AssetId(), skarbonka.AssetId(), 100m, AtmDay), cancellationToken);
        var toBrokerage = await client.PostAsJsonAsync(
            TransfersUri, NewTransferRequest(sejf.AssetId(), xtbIke.CashAssetId, 100m, AtmDay), cancellationToken);
        var fromBrokerage = await client.PostAsJsonAsync(
            TransfersUri, NewTransferRequest(xtbIke.CashAssetId, sejf.AssetId(), 100m, AtmDay), cancellationToken);

        await toPlace.AssertInvalidTransferCounterpartAsync(cancellationToken);
        await toBrokerage.AssertInvalidTransferCounterpartAsync(cancellationToken);
        await fromBrokerage.AssertInvalidTransferCounterpartAsync(cancellationToken);
        Assert.Equal(1_000m, await client.GetBalanceAsync(sejf.AssetId(), cancellationToken));
        Assert.Equal(0m, await client.GetBalanceAsync(skarbonka.AssetId(), cancellationToken));
    }
}
