using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class SettleSavingsInterestEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Settle_WithPreviewValues_CreditsNetInterest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken, NewInterestAccountRequest());
        var preview = await client.GetSavingsInterestPreviewAsync(portfolioId, account.AssetId, cancellationToken);

        var response = await client.PostAsJsonAsync(
            SavingsInterestSettlementsUri(portfolioId, account.AssetId),
            NewSettleInterestBody(
                SeptemberEnd, preview.GetProperty("grossInterest").GetDecimal(), preview.GetProperty("tax").GetDecimal()),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var transactions = (await client.ListTransactionsAsync(portfolioId, account.AssetId, cancellationToken)).Items;
        Assert.Equal(2, transactions.Count);
        var credit = Assert.Single(transactions, t => t.SavingsInterestPeriodEnd is not null);
        Assert.Equal(TransactionType.Deposit, credit.Type);
        Assert.Equal(33.29m, credit.Quantity);
        Assert.Equal(1m, credit.UnitPrice);
        Assert.Equal(SeptemberEnd, credit.Date);
        Assert.Equal(SeptemberEnd, credit.SavingsInterestPeriodEnd);

        var settled = await client.GetSavingsAccountAsync(portfolioId, account.AssetId, cancellationToken);
        Assert.Equal(10_033.29m, settled.Balance);
        Assert.Equal(10_033.29m, (await client.GetAssetAsync(portfolioId, account.AssetId, cancellationToken)).Quantity);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(portfolioId, account.AssetId, cancellationToken);

        await using var dbContext = CreateDbContext(userId);
        var settlement = await dbContext.Set<SavingsInterestSettlement>().SingleAsync(s => s.AssetId == account.AssetId, cancellationToken);
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
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken, NewInterestAccountRequest());

        Assert.Equal(SeptemberEnd, await client.SettlePreviewedSavingsInterestAsync(portfolioId, account.AssetId, cancellationToken));
        var preview = await client.GetSavingsInterestPreviewAsync(portfolioId, account.AssetId, cancellationToken);

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
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken, NewInterestAccountRequest());

        var response = await client.PostAsJsonAsync(
            SavingsInterestSettlementsUri(portfolioId, account.AssetId),
            NewSettleInterestBody(SeptemberEnd, 50.00m, 9.50m),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var credit = Assert.Single(
            (await client.ListTransactionsAsync(portfolioId, account.AssetId, cancellationToken)).Items,
            t => t.SavingsInterestPeriodEnd is not null);
        Assert.Equal(40.50m, credit.Quantity);
        Assert.Equal(10_040.50m, (await client.GetSavingsAccountAsync(portfolioId, account.AssetId, cancellationToken)).Balance);

        await using var dbContext = CreateDbContext(userId);
        var settlement = await dbContext.Set<SavingsInterestSettlement>().SingleAsync(s => s.AssetId == account.AssetId, cancellationToken);
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
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken, NewInterestAccountRequest());

        var response = await client.PostAsJsonAsync(
            SavingsInterestSettlementsUri(portfolioId, account.AssetId),
            NewSettleInterestBody(SeptemberEnd, 10.00m, 10.00m),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var asset = await client.GetAssetAsync(portfolioId, account.AssetId, cancellationToken);
        Assert.Equal(10_000m, asset.Quantity);
        Assert.Equal(1, asset.TransactionCount);

        await using (var dbContext = CreateDbContext(userId))
        {
            var settlement = await dbContext.Set<SavingsInterestSettlement>().SingleAsync(s => s.AssetId == account.AssetId, cancellationToken);
            Assert.Null(settlement.TransactionId);
            Assert.Equal(10.00m, settlement.GrossInterest);
            Assert.Equal(10.00m, settlement.Tax);
        }

        var preview = await client.GetSavingsInterestPreviewAsync(portfolioId, account.AssetId, cancellationToken);
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
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken, NewInterestAccountRequest());

        var response = await client.PostAsJsonAsync(
            SavingsInterestSettlementsUri(portfolioId, account.AssetId),
            NewSettleInterestBody(SeptemberEnd, grossInterest, tax),
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNothingSettledAsync(client, userId, portfolioId, account.AssetId, cancellationToken);
    }

    public static TheoryData<string, string> InvalidStates() => new()
    {
        { "nothing due", PortfolioAssertions.SavingsInterestNotDueErrorCode },
        { "same period settled twice", PortfolioAssertions.SavingsInterestPeriodMismatchErrorCode },
        { "a later period than the next due one", PortfolioAssertions.SavingsInterestPeriodMismatchErrorCode },
        { "archived portfolio", PortfolioAssertions.PortfolioArchivedErrorCode },
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
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken, NewInterestAccountRequest());
        var periodEnd = SeptemberEnd;
        var settledBefore = 0;

        switch (scenario)
        {
            case "same period settled twice":
                await client.SettleSavingsInterestAsync(portfolioId, account.AssetId, SeptemberEnd, 41.10m, 7.81m, cancellationToken);
                settledBefore = 1;
                break;
            case "a later period than the next due one":
                periodEnd = OctoberEnd;
                break;
            case "archived portfolio":
                await client.ArchivePortfolioAsync(portfolioId, cancellationToken);
                break;
        }

        var balanceBefore = (await client.GetAssetAsync(portfolioId, account.AssetId, cancellationToken)).Quantity;
        var transactionsBefore = (await client.ListTransactionsAsync(portfolioId, account.AssetId, cancellationToken)).TotalCount;

        var response = await client.PostAsJsonAsync(
            SavingsInterestSettlementsUri(portfolioId, account.AssetId),
            NewSettleInterestBody(periodEnd, 41.10m, 7.81m),
            cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, errorCode, cancellationToken);
        Assert.Equal(settledBefore, await CountSavingsSettlementsAsync(userId, cancellationToken, account.AssetId));
        var after = await client.GetAssetAsync(portfolioId, account.AssetId, cancellationToken);
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
        var request = NewInterestAccountRequest() with { Currency = "EUR" };
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken, request);

        await client.SettleSavingsInterestAsync(portfolioId, account.AssetId, SeptemberEnd, 41.10m, 7.81m, cancellationToken);

        await using var dbContext = CreateDbContext(userId);
        var credit = await dbContext.Transactions.SingleAsync(
            t => t.AssetId == account.AssetId && t.Date == SeptemberEnd, cancellationToken);
        Assert.Equal(33.29m, credit.Quantity);
        Assert.Equal(4.25m, credit.FxRateToPln);
    }

    [Fact]
    public async Task Settle_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            SavingsInterestSettlementsUri(Guid.NewGuid(), Guid.NewGuid()),
            NewSettleInterestBody(SeptemberEnd, 1m, 0m),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task AssertNothingSettledAsync(
        HttpClient client, Guid userId, Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        Assert.Equal(0, await CountSavingsSettlementsAsync(userId, cancellationToken, assetId));
        var asset = await client.GetAssetAsync(portfolioId, assetId, cancellationToken);
        Assert.Equal(10_000m, asset.Quantity);
        Assert.Equal(1, asset.TransactionCount);
    }
}
