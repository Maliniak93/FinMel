using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Bonds;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class AddBondEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Add_Valid_CreatesAssetTermsAndOpeningTransaction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var accountId = await client.CreateAccountAsync(cancellationToken, "Bonds", AccountType.Bonds);
        await client.FundAccountAsync(accountId, cancellationToken);

        var response = await client.PostAsJsonAsync(AllBondsUri, NewBondRequest() with { AccountId = accountId }, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BondResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(BondUri(body.AssetId), response.Headers.Location?.OriginalString);
        Assert.Equal(accountId, body.AccountId);
        Assert.Equal("Bonds", body.AccountName);
        Assert.Equal(TaxWrapper.Regular, body.TaxWrapper);
        Assert.Equal("EDO1036", body.SeriesCode);
        Assert.Equal(TreasuryBondType.Edo, body.Type);
        Assert.Equal(new DateOnly(2026, 10, 1), body.PurchaseDate);
        Assert.Equal(50, body.BondCount);
        Assert.Equal(100.00m, body.PurchasePricePerBond);
        Assert.Equal(5.35m, body.FirstPeriodRatePercent);
        Assert.Equal(2.00m, body.MarginPercent);
        Assert.Equal(3.00m, body.EarlyRedemptionFeePerBond);
        Assert.Equal(new DateOnly(2036, 10, 1), body.MaturityDate);
        Assert.Equal(5_000m, body.NominalValue);
        Assert.Equal(5_000.00m, body.BookValue);
        Assert.False(body.IsArchived);
        Assert.Equal(BondStatus.Active, body.Status);
        Assert.Equal(10, body.Periods.Count);

        var asset = await client.GetAssetAsync(body.AssetId, cancellationToken);
        Assert.Equal(AssetClass.Bond, asset.AssetClass);
        Assert.Equal(AssetValuationMode.CurrencyValued, asset.ValuationMode);
        Assert.Equal("PLN", asset.Currency);
        Assert.Equal(5_000.00m, asset.Quantity);
        Assert.Equal(1, asset.TransactionCount);

        var opening = Assert.Single((await client.ListTransactionsAsync(body.AssetId, cancellationToken)).Items);
        Assert.Equal(TransactionType.Deposit, opening.Type);
        Assert.Equal(5_000.00m, opening.Quantity);
        Assert.Equal(1m, opening.UnitPrice);
        Assert.Equal(new DateOnly(2026, 10, 1), opening.Date);
        Assert.Equal(5_000.00m, opening.ValuePln);
        Assert.NotNull(opening.Transfer);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(body.AssetId, cancellationToken);

        await using var dbContext = CreateDbContext(userId);
        var terms = await dbContext.Set<TreasuryBond>().SingleAsync(t => t.AssetId == body.AssetId, cancellationToken);
        Assert.Equal(userId, terms.UserId);
        Assert.Equal("EDO1036", terms.SeriesCode);
        Assert.Equal(TreasuryBondType.Edo, terms.Type);
        Assert.Equal(50, terms.BondCount);
        Assert.Equal(new DateOnly(2036, 10, 1), terms.MaturityDate);
    }

    [Fact]
    public async Task RequiresBondsAccount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var brokerageId = await client.CreateAccountAsync(cancellationToken, "XTB", AccountType.Brokerage);
        var bondsId = await client.CreateAccountAsync(cancellationToken, "Obligacje", AccountType.Bonds);
        await client.ArchiveAccountAsync(bondsId, cancellationToken);

        var withoutAccount = await client.PostAsJsonAsync(AllBondsUri, NewBondRequest(), cancellationToken);
        var intoBrokerage = await client.PostAsJsonAsync(AllBondsUri, NewBondRequest() with { AccountId = brokerageId }, cancellationToken);
        var intoArchived = await client.PostAsJsonAsync(AllBondsUri, NewBondRequest() with { AccountId = bondsId }, cancellationToken);
        var intoUnknown = await client.PostAsJsonAsync(AllBondsUri, NewBondRequest() with { AccountId = Guid.NewGuid() }, cancellationToken);

        await withoutAccount.AssertProblemAsync(HttpStatusCode.BadRequest, PortfolioAssertions.AccountRequiredErrorCode, cancellationToken);
        await intoBrokerage.AssertProblemAsync(HttpStatusCode.BadRequest, PortfolioAssertions.AccountTypeMismatchErrorCode, cancellationToken);
        await intoArchived.AssertAccountArchivedConflictAsync(cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, intoUnknown.StatusCode);
        await using var dbContext = CreateDbContext(userId);
        Assert.Equal(0, await dbContext.Assets.ExcludingAccountCash().CountAsync(cancellationToken));
    }

    [Fact]
    public async Task FundsFromAccountCash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.Zero));
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var bondsId = await client.CreateAccountAsync(cancellationToken, name: "Bonds", type: AccountType.Bonds);
        var cashId = await client.GetAccountCashAssetIdAsync(bondsId, cancellationToken);
        await client.RecordTransactionAsync(
            cashId, TransactionType.Deposit, 5_000m, DefaultTopUpDate, cancellationToken, unitPrice: 1m);

        var response = await client.PostAsJsonAsync(
            AllBondsUri,
            NewBondRequest(bondCount: 10, purchaseDate: CashDay1) with { AccountId = bondsId },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BondResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(4_000m, await client.GetCashBalanceAsync(bondsId, cancellationToken));
        Assert.Equal(1_000m, (await client.GetAssetAsync(body.AssetId, cancellationToken)).Quantity);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(cashId, cancellationToken);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(body.AssetId, cancellationToken);

        var withdraw = await client.GetCashWithdrawAsync(cashId, cancellationToken);
        Assert.Equal(1_000m, withdraw.Quantity);
        Assert.Equal(CashDay1, withdraw.Date);
        Assert.Equal(body.AssetId, withdraw.Transfer!.CounterpartAssetId);
        var opening = Assert.Single((await client.ListTransactionsAsync(body.AssetId, cancellationToken)).Items);
        Assert.Equal(TransactionType.Deposit, opening.Type);
        Assert.Equal(1_000m, opening.Quantity);
        Assert.Equal(CashDay1, opening.Date);
        Assert.Equal(cashId, opening.Transfer!.CounterpartAssetId);

        await using var dbContext = CreateDbContext(userId);
        var legs = await dbContext.Transactions.Where(t => t.TransferId != null).ToListAsync(cancellationToken);
        Assert.Equal(2, legs.Count);
        Assert.Single(legs.Select(t => t.TransferId).Distinct());
        Assert.Equal(withdraw.Id, Assert.Single(legs, t => t.AssetId == cashId).Id);
        Assert.Equal(opening.Id, Assert.Single(legs, t => t.AssetId == body.AssetId).Id);
    }

    [Fact]
    public async Task ExceedsAccountCash_Returns400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.Zero));
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var bondsId = await client.CreateAccountAsync(cancellationToken, name: "Bonds", type: AccountType.Bonds);
        var cashId = await client.GetAccountCashAssetIdAsync(bondsId, cancellationToken);
        await client.RecordTransactionAsync(
            cashId, TransactionType.Deposit, 5_000m, DefaultTopUpDate, cancellationToken, unitPrice: 1m);

        var response = await client.PostAsJsonAsync(
            AllBondsUri,
            NewBondRequest(bondCount: 60, purchaseDate: CashDay1) with { AccountId = bondsId },
            cancellationToken);

        await response.AssertInsufficientFundsAsync(cancellationToken);
        Assert.Equal(5_000m, await client.GetCashBalanceAsync(bondsId, cancellationToken));
        await using var dbContext = CreateDbContext(userId);
        Assert.Equal(0, await dbContext.Set<TreasuryBond>().CountAsync(cancellationToken));
        Assert.False(await dbContext.Assets.ExcludingAccountCash().AnyAsync(a => a.AccountId == bondsId, cancellationToken));
        Assert.False(await dbContext.Transactions.AnyAsync(t => t.TransferId != null, cancellationToken));
    }
}
