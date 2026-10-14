using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

// Preview, settle and undo are under test, so they are called directly; BankAccountsApi helpers only arrange.
public sealed class BankInterestSettlementEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Preview_EndedPeriod_ReturnsProjection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateInterestAccountAsync(cancellationToken);

        var response = await client.GetAsync(InterestPreviewUri(account.AccountId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.ReadJsonAsync(cancellationToken);
        Assert.Equal("2026-09-01", preview.GetProperty("periodStart").GetString());
        Assert.Equal("2026-09-30", preview.GetProperty("periodEnd").GetString());
        Assert.Equal(5m, preview.GetProperty("annualInterestRatePercent").GetDecimal());
        Assert.Equal(10_000.00m, preview.GetProperty("averageDailyBalance").GetDecimal());
        Assert.Equal(41.10m, preview.GetProperty("grossInterest").GetDecimal());
        Assert.Equal(7.81m, preview.GetProperty("tax").GetDecimal());
        Assert.Equal(33.29m, preview.GetProperty("netInterest").GetDecimal());
        Assert.Equal(1, preview.GetProperty("duePeriodCount").GetInt32());
    }

    [Fact]
    public async Task Preview_NothingDue_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberLastDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateInterestAccountAsync(cancellationToken);

        var response = await client.GetAsync(InterestPreviewUri(account.AccountId), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.BankInterestNotDueErrorCode, cancellationToken);
    }

    [Fact]
    public async Task Preview_SeveralMonthsEnded_OffersTheOldestAndCountsAll()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(NovemberEndedUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateInterestAccountAsync(cancellationToken);

        var preview = await client.GetInterestPreviewAsync(account.AccountId, cancellationToken);

        Assert.Equal("2026-09-30", preview.GetProperty("periodEnd").GetString());
        Assert.Equal(3, preview.GetProperty("duePeriodCount").GetInt32());
    }

    [Fact]
    public async Task Preview_ZeroRateAccount_IsNeverDue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(NovemberEndedUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateInterestAccountAsync(cancellationToken, annualInterestRatePercent: 0m);

        var response = await client.GetAsync(InterestPreviewUri(account.AccountId), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.BankInterestNotDueErrorCode, cancellationToken);
    }

    [Fact]
    public async Task Preview_CountsTransferFromItsDate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var current = await client.CreateBankAccountAsync(
            cancellationToken, "mBank", balance: 10_000m, depositedOn: InterestOpeningDate);
        var savings = await client.CreateInterestAccountAsync(cancellationToken, "Nest Oszczędnościowe", balance: 5_000m);
        await client.CreateTransferAsync(
            cancellationToken, NewTransferRequest(current.MoneyAssetId, savings.MoneyAssetId, 5_000m, new DateOnly(2026, 9, 16)));

        var preview = await client.GetInterestPreviewAsync(savings.AccountId, cancellationToken);

        Assert.Equal(7_500.00m, preview.GetProperty("averageDailyBalance").GetDecimal());
        Assert.Equal(30.82m, preview.GetProperty("grossInterest").GetDecimal());
    }

    [Fact]
    public async Task Preview_CountsDepositPayoutFromItsDate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var deposit = await client.AddDepositAsync(cancellationToken);
        await client.SettleDepositAsync(
            deposit.AssetId, cancellationToken, NewSettleRequest(grossInterest: 0m, tax: 0m));
        var account = await client.CreateBankAccountAsync(
            cancellationToken, "Nest Oszczędnościowe", SavingsVariant, annualInterestRatePercent: 5m);
        await client.PayOutDepositAsync(
            deposit.AssetId, account.MoneyAssetId, cancellationToken, new DateOnly(2026, 9, 16));

        var preview = await client.GetInterestPreviewAsync(account.AccountId, cancellationToken);

        Assert.Equal("2026-09-16", preview.GetProperty("periodStart").GetString());
        Assert.Equal("2026-09-30", preview.GetProperty("periodEnd").GetString());
        Assert.Equal(10_000.00m, preview.GetProperty("averageDailyBalance").GetDecimal());
        Assert.Equal(20.55m, preview.GetProperty("grossInterest").GetDecimal());
    }

    [Fact]
    public async Task Preview_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync(InterestPreviewUri(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Settle_WithPreviewValues_CreditsNetInterest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var account = await client.CreateInterestAccountAsync(cancellationToken);
        var preview = await client.GetInterestPreviewAsync(account.AccountId, cancellationToken);

        var response = await client.PostAsJsonAsync(
            InterestSettlementsUri(account.AccountId),
            NewSettleInterestBody(
                SeptemberEnd, preview.GetProperty("grossInterest").GetDecimal(), preview.GetProperty("tax").GetDecimal()),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var transactions = (await client.ListTransactionsAsync(account.MoneyAssetId, cancellationToken)).Items;
        Assert.Equal(2, transactions.Count);
        var credit = Assert.Single(transactions, t => t.BankInterestPeriodEnd is not null);
        Assert.Equal(TransactionType.Deposit, credit.Type);
        Assert.Equal(33.29m, credit.Quantity);
        Assert.Equal(1m, credit.UnitPrice);
        Assert.Equal(SeptemberEnd, credit.Date);
        Assert.Equal(SeptemberEnd, credit.BankInterestPeriodEnd);

        Assert.Equal(10_033.29m, await client.GetCashBalanceAsync(account.AccountId, cancellationToken));
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(account.MoneyAssetId, cancellationToken);

        await using var dbContext = CreateDbContext(userId);
        var settlement = await dbContext.Set<BankInterestSettlement>().SingleAsync(s => s.AssetId == account.MoneyAssetId, cancellationToken);
        Assert.Equal(new DateOnly(2026, 9, 1), settlement.PeriodStart);
        Assert.Equal(SeptemberEnd, settlement.PeriodEnd);
        Assert.Equal(41.10m, settlement.GrossInterest);
        Assert.Equal(7.81m, settlement.Tax);
        Assert.Equal(credit.Id, settlement.TransactionId);
    }

    [Fact]
    public async Task Settle_NextPeriodCompoundsTheCredit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(OctoberEndedUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateInterestAccountAsync(cancellationToken);

        Assert.Equal(SeptemberEnd, await client.SettlePreviewedInterestAsync(account.AccountId, cancellationToken));
        var preview = await client.GetInterestPreviewAsync(account.AccountId, cancellationToken);

        Assert.Equal("2026-10-01", preview.GetProperty("periodStart").GetString());
        Assert.Equal("2026-10-31", preview.GetProperty("periodEnd").GetString());
        Assert.Equal(10_033.29m, preview.GetProperty("averageDailyBalance").GetDecimal());
        Assert.Equal(42.61m, preview.GetProperty("grossInterest").GetDecimal());
        Assert.Equal(8.10m, preview.GetProperty("tax").GetDecimal());
        Assert.Equal(34.51m, preview.GetProperty("netInterest").GetDecimal());
        Assert.Equal(1, preview.GetProperty("duePeriodCount").GetInt32());
    }

    [Fact]
    public async Task Settle_WithOverriddenAmounts_UsesThem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var account = await client.CreateInterestAccountAsync(cancellationToken);

        var response = await client.PostAsJsonAsync(
            InterestSettlementsUri(account.AccountId),
            NewSettleInterestBody(SeptemberEnd, 50.00m, 9.50m),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var credit = Assert.Single(
            (await client.ListTransactionsAsync(account.MoneyAssetId, cancellationToken)).Items,
            t => t.BankInterestPeriodEnd is not null);
        Assert.Equal(40.50m, credit.Quantity);
        Assert.Equal(10_040.50m, await client.GetCashBalanceAsync(account.AccountId, cancellationToken));

        await using var dbContext = CreateDbContext(userId);
        var settlement = await dbContext.Set<BankInterestSettlement>().SingleAsync(s => s.AssetId == account.MoneyAssetId, cancellationToken);
        Assert.Equal(50.00m, settlement.GrossInterest);
        Assert.Equal(9.50m, settlement.Tax);
    }

    [Fact]
    public async Task Settle_ZeroNet_StoresSettlementWithoutTransaction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(OctoberEndedUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var account = await client.CreateInterestAccountAsync(cancellationToken);

        var response = await client.PostAsJsonAsync(
            InterestSettlementsUri(account.AccountId),
            NewSettleInterestBody(SeptemberEnd, 10.00m, 10.00m),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var asset = await client.GetAssetAsync(account.MoneyAssetId, cancellationToken);
        Assert.Equal(10_000m, asset.Quantity);
        Assert.Equal(1, asset.TransactionCount);

        await using (var dbContext = CreateDbContext(userId))
        {
            var settlement = await dbContext.Set<BankInterestSettlement>().SingleAsync(s => s.AssetId == account.MoneyAssetId, cancellationToken);
            Assert.Null(settlement.TransactionId);
            Assert.Equal(10.00m, settlement.GrossInterest);
            Assert.Equal(10.00m, settlement.Tax);
        }

        var preview = await client.GetInterestPreviewAsync(account.AccountId, cancellationToken);
        Assert.Equal("2026-10-31", preview.GetProperty("periodEnd").GetString());
        Assert.Equal(1, preview.GetProperty("duePeriodCount").GetInt32());
    }

    public static TheoryData<string, decimal, decimal> InvalidAmounts() => new()
    {
        { "negative gross", -1.00m, 0m },
        { "tax above gross", 10.00m, 10.01m },
        { "negative tax", 10.00m, -0.01m },
    };

    [Theory]
    [MemberData(nameof(InvalidAmounts))]
    public async Task Settle_InvalidInput_ReturnsBadRequest(string scenario, decimal grossInterest, decimal tax)
    {
        Assert.NotEmpty(scenario);
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var account = await client.CreateInterestAccountAsync(cancellationToken);

        var response = await client.PostAsJsonAsync(
            InterestSettlementsUri(account.AccountId),
            NewSettleInterestBody(SeptemberEnd, grossInterest, tax),
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountBankSettlementsAsync(userId, cancellationToken, account.MoneyAssetId));
        var asset = await client.GetAssetAsync(account.MoneyAssetId, cancellationToken);
        Assert.Equal(10_000m, asset.Quantity);
        Assert.Equal(1, asset.TransactionCount);
    }

    public static TheoryData<string, string> InvalidStates() => new()
    {
        { "nothing due", PortfolioAssertions.BankInterestNotDueErrorCode },
        { "same period settled twice", PortfolioAssertions.BankInterestPeriodMismatchErrorCode },
        { "a later period than the next due one", PortfolioAssertions.BankInterestPeriodMismatchErrorCode },
    };

    [Theory]
    [MemberData(nameof(InvalidStates))]
    public async Task Settle_InvalidState_ReturnsConflict(string scenario, string errorCode)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // "nothing due" runs while September is still open; the others need September and October ended.
        Factory.Clock.SetUtcNow(scenario == "nothing due" ? SeptemberLastDayUtc : OctoberEndedUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var account = await client.CreateInterestAccountAsync(cancellationToken);
        var periodEnd = SeptemberEnd;
        var settledBefore = 0;

        switch (scenario)
        {
            case "same period settled twice":
                await client.SettleInterestAsync(account.AccountId, SeptemberEnd, 41.10m, 7.81m, cancellationToken);
                settledBefore = 1;
                break;
            case "a later period than the next due one":
                periodEnd = OctoberEnd;
                break;
        }

        var balanceBefore = (await client.GetAssetAsync(account.MoneyAssetId, cancellationToken)).Quantity;
        var transactionsBefore = (await client.ListTransactionsAsync(account.MoneyAssetId, cancellationToken)).TotalCount;

        var response = await client.PostAsJsonAsync(
            InterestSettlementsUri(account.AccountId),
            NewSettleInterestBody(periodEnd, 41.10m, 7.81m),
            cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, errorCode, cancellationToken);
        Assert.Equal(settledBefore, await CountBankSettlementsAsync(userId, cancellationToken, account.MoneyAssetId));
        var after = await client.GetAssetAsync(account.MoneyAssetId, cancellationToken);
        Assert.Equal(balanceBefore, after.Quantity);
        Assert.Equal(transactionsBefore, after.TransactionCount);
    }

    [Fact]
    public async Task Settle_ForeignCurrencyAccount_FreezesFxRateOfPeriodEnd()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        Factory.FxRateLookupClient
            .WithRate("EUR", InterestOpeningDate, 4.30m)
            .WithRate("EUR", SeptemberEnd, 4.25m);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var account = await client.CreateInterestAccountAsync(cancellationToken, "Nest EUR", currency: "EUR");

        await client.SettleInterestAsync(account.AccountId, SeptemberEnd, 41.10m, 7.81m, cancellationToken);

        await using var dbContext = CreateDbContext(userId);
        var credit = await dbContext.Transactions.SingleAsync(
            t => t.AssetId == account.MoneyAssetId && t.Date == SeptemberEnd, cancellationToken);
        Assert.Equal(33.29m, credit.Quantity);
        Assert.Equal(4.25m, credit.FxRateToPln);
    }

    [Fact]
    public async Task ListAccounts_FlagsInterestDue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var settledUp = await client.CreateInterestAccountAsync(cancellationToken, "Settled up");
        var due = await client.CreateInterestAccountAsync(cancellationToken, "Due");
        var brokerage = await client.CreateAccountAsync(cancellationToken);
        await client.SettlePreviewedInterestAsync(settledUp.AccountId, cancellationToken);

        var response = await client.GetAsync(AccountsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var accounts = (await response.ReadJsonAsync(cancellationToken)).EnumerateArray().ToList();
        var dueBank = accounts.Single(a => a.GetProperty("id").GetGuid() == due.AccountId).Bank();
        Assert.True(dueBank.GetProperty("interestDue").GetBoolean());
        Assert.Equal(1, dueBank.GetProperty("duePeriodCount").GetInt32());
        Assert.False(accounts.Single(a => a.GetProperty("id").GetGuid() == settledUp.AccountId).Bank().GetProperty("interestDue").GetBoolean());
        Assert.False(accounts.Single(a => a.GetProperty("id").GetGuid() == brokerage).TryGetProperty("bank", out var bank) && bank.ValueKind != System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Settle_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            InterestSettlementsUri(Guid.NewGuid()),
            NewSettleInterestBody(SeptemberEnd, 1m, 0m),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Undo_Latest_RemovesCreditAndReopensPeriod()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(OctoberEndedUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var account = await client.CreateInterestAccountAsync(cancellationToken);
        await client.SettlePreviewedInterestAsync(account.AccountId, cancellationToken);
        var septemberSettlementId = await client.GetLastSettlementIdAsync(account.AccountId, cancellationToken);
        await client.SettlePreviewedInterestAsync(account.AccountId, cancellationToken);
        var octoberSettlementId = await client.GetLastSettlementIdAsync(account.AccountId, cancellationToken);
        Assert.NotEqual(septemberSettlementId, octoberSettlementId);
        Assert.Equal(10_067.80m, await client.GetCashBalanceAsync(account.AccountId, cancellationToken));

        var response = await client.DeleteAsync(InterestSettlementUri(account.AccountId, octoberSettlementId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(10_033.29m, await client.GetCashBalanceAsync(account.AccountId, cancellationToken));
        Assert.Equal(septemberSettlementId, await client.GetLastSettlementIdAsync(account.AccountId, cancellationToken));
        var transactions = (await client.ListTransactionsAsync(account.MoneyAssetId, cancellationToken)).Items;
        Assert.Equal(2, transactions.Count);
        Assert.DoesNotContain(transactions, t => t.BankInterestPeriodEnd == OctoberEnd);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(account.MoneyAssetId, cancellationToken);
        Assert.Equal(1, await CountBankSettlementsAsync(userId, cancellationToken, account.MoneyAssetId));

        var preview = await client.GetInterestPreviewAsync(account.AccountId, cancellationToken);
        Assert.Equal("2026-10-31", preview.GetProperty("periodEnd").GetString());
    }

    [Fact]
    public async Task Undo_NotLatest_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(OctoberEndedUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var account = await client.CreateInterestAccountAsync(cancellationToken);
        await client.SettlePreviewedInterestAsync(account.AccountId, cancellationToken);
        var septemberSettlementId = await client.GetLastSettlementIdAsync(account.AccountId, cancellationToken);
        await client.SettlePreviewedInterestAsync(account.AccountId, cancellationToken);

        var response = await client.DeleteAsync(InterestSettlementUri(account.AccountId, septemberSettlementId), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.BankSettlementNotLatestErrorCode, cancellationToken);
        Assert.Equal(2, await CountBankSettlementsAsync(userId, cancellationToken, account.MoneyAssetId));
        Assert.Equal(10_067.80m, await client.GetCashBalanceAsync(account.AccountId, cancellationToken));
        Assert.Equal(3, (await client.ListTransactionsAsync(account.MoneyAssetId, cancellationToken)).TotalCount);
    }

    [Fact]
    public async Task Undo_CreditAlreadySpent_IsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var account = await client.CreateInterestAccountAsync(cancellationToken);
        await client.SettlePreviewedInterestAsync(account.AccountId, cancellationToken);
        var settlementId = await client.GetLastSettlementIdAsync(account.AccountId, cancellationToken);
        await client.RecordTransactionAsync(
            account.MoneyAssetId, TransactionType.Withdraw, 10_033.29m, new DateOnly(2026, 10, 1), cancellationToken, unitPrice: 1m);

        var response = await client.DeleteAsync(InterestSettlementUri(account.AccountId, settlementId), cancellationToken);

        await response.AssertInsufficientFundsAsync(cancellationToken);
        Assert.Equal(1, await CountBankSettlementsAsync(userId, cancellationToken, account.MoneyAssetId));
        Assert.Equal(0m, await client.GetCashBalanceAsync(account.AccountId, cancellationToken));
        Assert.Contains(
            (await client.ListTransactionsAsync(account.MoneyAssetId, cancellationToken)).Items,
            t => t.BankInterestPeriodEnd == SeptemberEnd);
    }

    [Fact]
    public async Task Undo_ZeroNetSettlement_RemovesItAndKeepsTheBalance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var account = await client.CreateInterestAccountAsync(cancellationToken);
        await client.SettleInterestAsync(account.AccountId, SeptemberEnd, 10.00m, 10.00m, cancellationToken);
        var settlementId = await client.GetLastSettlementIdAsync(account.AccountId, cancellationToken);

        var response = await client.DeleteAsync(InterestSettlementUri(account.AccountId, settlementId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, await CountBankSettlementsAsync(userId, cancellationToken, account.MoneyAssetId));
        var asset = await client.GetAssetAsync(account.MoneyAssetId, cancellationToken);
        Assert.Equal(10_000m, asset.Quantity);
        Assert.Equal(1, asset.TransactionCount);
        var preview = await client.GetInterestPreviewAsync(account.AccountId, cancellationToken);
        Assert.Equal("2026-09-30", preview.GetProperty("periodEnd").GetString());
    }

    [Fact]
    public async Task Undo_UnknownSettlement_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateInterestAccountAsync(cancellationToken);

        var response = await client.DeleteAsync(InterestSettlementUri(account.AccountId, Guid.NewGuid()), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
