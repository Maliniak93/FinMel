using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class RollOverDepositEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    private static readonly DateTimeOffset BeforeDefaultMaturityUtc = new(2026, 4, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RollOver_DueDeposit_SettlesAndStartsNextTerm()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(
            cancellationToken, NewDepositRequest(earlyBreakInterestLossPercent: 50m));
        var openingId = Assert.Single((await client.ListTransactionsAsync(portfolioId, deposit.AssetId, cancellationToken)).Items).Id;
        var preview = await client.GetSettlementPreviewAsync(portfolioId, deposit.AssetId, cancellationToken);
        Assert.Equal(119.83m, preview.NetInterest);

        var response = await client.PostAsJsonAsync(
            RollOverDepositUri(portfolioId, deposit.AssetId),
            NewRollOverRequest(annualInterestRatePercent: 5.5m, grossInterest: preview.GrossInterest, tax: preview.Tax),
            cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Roll over answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");

        var transactions = (await client.ListTransactionsAsync(portfolioId, deposit.AssetId, cancellationToken)).Items;
        Assert.Equal(2, transactions.Count);
        var credit = Assert.Single(transactions, t => t.Id != openingId);
        Assert.Equal(TransactionType.Deposit, credit.Type);
        Assert.Equal(119.83m, credit.Quantity);
        Assert.Equal(1m, credit.UnitPrice);
        Assert.Equal(new DateOnly(2026, 4, 15), credit.Date);
        Assert.Null(credit.Transfer);

        var asset = await client.GetAssetAsync(portfolioId, deposit.AssetId, cancellationToken);
        Assert.Equal(10_119.83m, asset.Quantity);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(portfolioId, deposit.AssetId, cancellationToken);

        var rolled = await client.GetDepositAsync(portfolioId, deposit.AssetId, cancellationToken);
        Assert.Equal(10_119.83m, rolled.Principal);
        Assert.Equal(new DateOnly(2026, 4, 15), rolled.StartDate);
        Assert.Equal(new DateOnly(2026, 7, 15), rolled.MaturityDate);
        Assert.Equal(5.5m, rolled.AnnualInterestRatePercent);
        Assert.Equal(3, rolled.TermLength);
        Assert.Equal(DepositTermUnit.Months, rolled.TermUnit);
        Assert.Equal(DepositCapitalization.AtMaturity, rolled.Capitalization);
        Assert.False(rolled.TaxExempt);
        Assert.Equal(50m, rolled.EarlyBreakInterestLossPercent);
        Assert.Equal("Test bank", rolled.BankName);
        Assert.Equal("Term deposit", rolled.Name);
        Assert.Null(rolled.SettledOn);
        Assert.Null(rolled.SettledGrossInterest);
        Assert.Null(rolled.SettledTax);
        Assert.Equal(DepositStatus.Active, rolled.Status);
        Assert.Equal(1, rolled.RolloverCount);
        Assert.Null(rolled.PaidOutOn);
    }

    [Fact]
    public async Task RollOver_ForeignCurrencyDueDeposit_FreezesFxRateOfMaturityDate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        Factory.FxRateLookupClient
            .WithRate("EUR", new DateOnly(2026, 1, 15), 4.30m)
            .WithRate("EUR", new DateOnly(2026, 4, 15), 4.25m)
            .WithRate("EUR", new DateOnly(2026, 4, 20), 4.10m);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken, NewDepositRequest(currency: "EUR"));

        var response = await client.PostAsJsonAsync(
            RollOverDepositUri(portfolioId, deposit.AssetId), NewRollOverRequest(), cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Roll over answered {(int)response.StatusCode}.");
        await using var dbContext = CreateDbContext(userId);
        var transactions = await dbContext.Transactions
            .Where(t => t.AssetId == deposit.AssetId)
            .OrderBy(t => t.Date)
            .ToListAsync(cancellationToken);
        Assert.Equal(2, transactions.Count);
        Assert.Equal(4.30m, transactions[0].FxRateToPln);
        Assert.Equal(new DateOnly(2026, 4, 15), transactions[1].Date);
        Assert.Equal(119.83m, transactions[1].Quantity);
        Assert.Equal(4.25m, transactions[1].FxRateToPln);
    }

    [Fact]
    public async Task RollOver_SettledDeposit_StartsNextTermWithoutNewTransaction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        await client.SettleDepositAsync(
            portfolioId,
            deposit.AssetId,
            cancellationToken,
            NewSettleRequest(settledOn: new DateOnly(2026, 4, 17), grossInterest: 150.00m, tax: 28.50m));
        var rowsBefore = await SnapshotUserRowsAsync(userId, cancellationToken);
        var transactionsBefore = (await client.ListTransactionsAsync(portfolioId, deposit.AssetId, cancellationToken)).Items;

        var response = await client.PostAsJsonAsync(
            RollOverDepositUri(portfolioId, deposit.AssetId), SettledRollOverBody(annualInterestRatePercent: 5.5m), cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Roll over answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");

        Assert.Equal(rowsBefore, await SnapshotUserRowsAsync(userId, cancellationToken));
        var transactionsAfter = (await client.ListTransactionsAsync(portfolioId, deposit.AssetId, cancellationToken)).Items;
        Assert.Equal(
            transactionsBefore.Select(t => (t.Id, t.Type, t.Quantity, t.Date)).OrderBy(t => t.Id),
            transactionsAfter.Select(t => (t.Id, t.Type, t.Quantity, t.Date)).OrderBy(t => t.Id));
        Assert.Equal(10_121.50m, (await client.GetAssetAsync(portfolioId, deposit.AssetId, cancellationToken)).Quantity);

        var rolled = await client.GetDepositAsync(portfolioId, deposit.AssetId, cancellationToken);
        Assert.Equal(10_121.50m, rolled.Principal);
        Assert.Equal(new DateOnly(2026, 4, 15), rolled.StartDate);
        Assert.Equal(new DateOnly(2026, 7, 15), rolled.MaturityDate);
        Assert.Equal(5.5m, rolled.AnnualInterestRatePercent);
        Assert.Null(rolled.SettledOn);
        Assert.Null(rolled.SettledGrossInterest);
        Assert.Null(rolled.SettledTax);
        Assert.Equal(DepositStatus.Active, rolled.Status);
        Assert.Equal(1, rolled.RolloverCount);

        await using var dbContext = CreateDbContext(userId);
        var terms = await dbContext.Set<TermDeposit>().SingleAsync(t => t.AssetId == deposit.AssetId, cancellationToken);
        Assert.Equal(1, terms.RolloverCount);
        Assert.Equal(10_121.50m, terms.Principal);
    }

    [Fact]
    public async Task RollOver_LongOverdueDeposit_IsDueAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken, NewDepositRequest(termLength: 1));
        Assert.Equal(new DateOnly(2026, 2, 15), deposit.MaturityDate);
        var preview = await client.GetSettlementPreviewAsync(portfolioId, deposit.AssetId, cancellationToken);

        var response = await client.PostAsJsonAsync(
            RollOverDepositUri(portfolioId, deposit.AssetId),
            NewRollOverRequest(grossInterest: preview.GrossInterest, tax: preview.Tax),
            cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Roll over answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");

        var listResponse = await client.GetAsync(AllDepositsUri, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listed = Assert.Single((await listResponse.Content.ReadFromJsonAsync<List<DepositResponse>>(cancellationToken))!);
        Assert.Equal(deposit.AssetId, listed.AssetId);
        Assert.Equal(new DateOnly(2026, 2, 15), listed.StartDate);
        Assert.Equal(new DateOnly(2026, 3, 15), listed.MaturityDate);
        Assert.True(listed.MaturityDate <= new DateOnly(2026, 4, 20));
        Assert.Equal(DepositStatus.Due, listed.Status);
        Assert.Equal(10_000m + preview.NetInterest, listed.Principal);
        Assert.Equal(1, listed.RolloverCount);
        Assert.Null(listed.SettledOn);
    }

    [Theory]
    [InlineData("active", PortfolioAssertions.DepositNotDueErrorCode)]
    [InlineData("paid-out", PortfolioAssertions.DepositAlreadyPaidOutErrorCode)]
    [InlineData("archived-portfolio", PortfolioAssertions.PortfolioArchivedErrorCode)]
    [InlineData("archived-asset", PortfolioAssertions.AssetArchivedErrorCode)]
    [InlineData("settled-with-amounts", PortfolioAssertions.DepositAlreadySettledErrorCode)]
    [InlineData("settled-with-gross-only", PortfolioAssertions.DepositAlreadySettledErrorCode)]
    [InlineData("settled-with-tax-only", PortfolioAssertions.DepositAlreadySettledErrorCode)]
    public async Task RollOver_InvalidState_ReturnsConflict(string invalidCase, string errorCode)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(invalidCase == "active" ? BeforeDefaultMaturityUtc : AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        Guid portfolioId;
        Guid assetId;
        object body;
        switch (invalidCase)
        {
            case "active":
                (portfolioId, var active) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
                assetId = active.AssetId;
                body = NewRollOverRequest();
                break;
            case "paid-out":
                var paidOut = await client.CreatePaidOutDepositAsync(cancellationToken);
                (portfolioId, assetId) = (paidOut.DepositPortfolioId, paidOut.Deposit.AssetId);
                body = SettledRollOverBody();
                break;
            case "archived-portfolio":
                (portfolioId, var archived) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
                assetId = archived.AssetId;
                await client.ArchivePortfolioAsync(portfolioId, cancellationToken);
                body = NewRollOverRequest();
                break;
            case "archived-asset":
                (portfolioId, var shelved) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
                assetId = shelved.AssetId;
                await client.ArchiveAssetAsync(portfolioId, assetId, cancellationToken);
                body = NewRollOverRequest();
                break;
            case "settled-with-amounts":
            case "settled-with-gross-only":
            case "settled-with-tax-only":
                (portfolioId, var settled) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
                assetId = settled.AssetId;
                await client.SettleDepositAsync(portfolioId, assetId, cancellationToken);
                body = invalidCase switch
                {
                    "settled-with-amounts" => NewRollOverRequest(),
                    "settled-with-gross-only" => NewRollOverRequest(tax: null),
                    _ => NewRollOverRequest(grossInterest: null)
                };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(invalidCase), invalidCase, null);
        }

        var before = await SnapshotDepositAsync(client, userId, portfolioId, assetId, cancellationToken);

        var response = await client.PostAsJsonAsync(RollOverDepositUri(portfolioId, assetId), body, cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, errorCode, cancellationToken);
        await AssertDepositUnchangedAsync(client, userId, portfolioId, assetId, before, cancellationToken);
        Assert.Equal(0, before.Deposit.RolloverCount);
    }

    [Theory]
    [InlineData("gross-missing")]
    [InlineData("tax-missing")]
    [InlineData("both-missing")]
    [InlineData("tax-over-gross")]
    [InlineData("gross-3dp")]
    [InlineData("tax-3dp")]
    [InlineData("gross-negative")]
    [InlineData("tax-negative")]
    [InlineData("rate-negative")]
    [InlineData("rate-over-100")]
    [InlineData("rate-5dp")]
    public async Task RollOver_InvalidInput_ReturnsBadRequest(string invalidCase)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        object body = invalidCase switch
        {
            "gross-missing" => NewRollOverRequest(grossInterest: null),
            "tax-missing" => NewRollOverRequest(tax: null),
            "both-missing" => SettledRollOverBody(),
            "tax-over-gross" => NewRollOverRequest(grossInterest: 147.95m, tax: 147.96m),
            "gross-3dp" => NewRollOverRequest(grossInterest: 147.951m),
            "tax-3dp" => NewRollOverRequest(tax: 28.125m),
            "gross-negative" => NewRollOverRequest(grossInterest: -1m, tax: 0m),
            "tax-negative" => NewRollOverRequest(tax: -0.01m),
            "rate-negative" => NewRollOverRequest(annualInterestRatePercent: -0.5m),
            "rate-over-100" => NewRollOverRequest(annualInterestRatePercent: 100.01m),
            "rate-5dp" => NewRollOverRequest(annualInterestRatePercent: 5.12345m),
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase), invalidCase, null)
        };
        var before = await SnapshotDepositAsync(client, userId, portfolioId, deposit.AssetId, cancellationToken);

        var response = await client.PostAsJsonAsync(RollOverDepositUri(portfolioId, deposit.AssetId), body, cancellationToken);

        if (invalidCase.EndsWith("-missing", StringComparison.Ordinal))
        {
            await response.AssertProblemAsync(
                HttpStatusCode.BadRequest, PortfolioAssertions.SettlementAmountsRequiredErrorCode, cancellationToken);
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        await AssertDepositUnchangedAsync(client, userId, portfolioId, deposit.AssetId, before, cancellationToken);
        Assert.Equal(DepositStatus.Due, before.Deposit.Status);
    }

    [Fact]
    public async Task RollOver_UnknownOrNonDepositAsset_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        var cashId = await client.AddCashAssetAsync(portfolioId, cancellationToken);

        var unknown = await client.PostAsJsonAsync(RollOverDepositUri(portfolioId, Guid.NewGuid()), NewRollOverRequest(), cancellationToken);
        var cash = await client.PostAsJsonAsync(RollOverDepositUri(portfolioId, cashId), NewRollOverRequest(), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, cash.StatusCode);
        var cashAsset = await client.GetAssetAsync(portfolioId, cashId, cancellationToken);
        Assert.Equal(0m, cashAsset.Quantity);
        Assert.Equal(0, cashAsset.TransactionCount);

        // Control: the route answers for a real deposit — the 404s were about the asset, not a missing route.
        var real = await client.PostAsJsonAsync(RollOverDepositUri(portfolioId, deposit.AssetId), NewRollOverRequest(), cancellationToken);
        Assert.True(real.IsSuccessStatusCode, $"Rolling over the real deposit answered {(int)real.StatusCode}.");
    }

    [Fact]
    public async Task RollOver_NextTerm_CanBeSettledRolledAgainAndPaidOut()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        var walletId = await client.CreatePortfolioAsync(cancellationToken, name: "Wallet");
        var cashId = await client.AddCashAssetAsync(walletId, cancellationToken);

        // Period 1 → 2: 10 119.83 from 2026-04-15 to 2026-07-15 at 5.5 %.
        var first = await client.PostAsJsonAsync(RollOverDepositUri(portfolioId, deposit.AssetId), NewRollOverRequest(), cancellationToken);
        Assert.True(first.IsSuccessStatusCode, $"The first roll over answered {(int)first.StatusCode}.");
        Assert.Equal(DepositStatus.Active, (await client.GetDepositAsync(portfolioId, deposit.AssetId, cancellationToken)).Status);

        // Warsaw 2026-07-20 12:00 — past the new maturity.
        Factory.Clock.SetUtcNow(new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero));
        var due = await client.GetDepositAsync(portfolioId, deposit.AssetId, cancellationToken);
        Assert.Equal(DepositStatus.Due, due.Status);

        var preview = await client.GetSettlementPreviewAsync(portfolioId, deposit.AssetId, cancellationToken);
        var expected = DepositInterestMath.Project(new DepositTerms
        {
            Principal = 10_119.83m,
            StartDate = new DateOnly(2026, 4, 15),
            TermLength = 3,
            TermUnit = DepositTermUnit.Months,
            AnnualInterestRatePercent = 5.5m,
            Capitalization = DepositCapitalization.AtMaturity,
            TaxExempt = false
        });
        Assert.Equal(new DateOnly(2026, 7, 15), preview.SettledOn);
        Assert.Equal(expected.GrossInterest, preview.GrossInterest);
        Assert.Equal(expected.Tax, preview.Tax);
        Assert.Equal(expected.NetInterest, preview.NetInterest);
        Assert.Equal(expected.FinalAmount, preview.FinalAmount);
        Assert.True(preview.GrossInterest > 0m);

        // Before the new start date is refused; on/after it is accepted.
        var beforeNewStart = await client.PostAsJsonAsync(
            SettleDepositUri(portfolioId, deposit.AssetId),
            NewSettleRequest(settledOn: new DateOnly(2026, 4, 14), grossInterest: preview.GrossInterest, tax: preview.Tax),
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, beforeNewStart.StatusCode);

        var settle = await client.PostAsJsonAsync(
            SettleDepositUri(portfolioId, deposit.AssetId),
            NewSettleRequest(settledOn: new DateOnly(2026, 7, 16), grossInterest: preview.GrossInterest, tax: preview.Tax),
            cancellationToken);
        Assert.True(settle.IsSuccessStatusCode, $"Settling the rolled-over term answered {(int)settle.StatusCode}.");
        var balanceAfterSecondTerm = 10_119.83m + preview.NetInterest;
        Assert.Equal(balanceAfterSecondTerm, (await client.GetAssetAsync(portfolioId, deposit.AssetId, cancellationToken)).Quantity);
        Assert.Equal(DepositStatus.Settled, (await client.GetDepositAsync(portfolioId, deposit.AssetId, cancellationToken)).Status);

        // Period 2 → 3, from Settled: count 2, the whole balance, starting on the 2026-07-15 maturity.
        var second = await client.PostAsJsonAsync(
            RollOverDepositUri(portfolioId, deposit.AssetId), SettledRollOverBody(annualInterestRatePercent: 5m), cancellationToken);
        Assert.True(second.IsSuccessStatusCode, $"The second roll over answered {(int)second.StatusCode}.");
        var third = await client.GetDepositAsync(portfolioId, deposit.AssetId, cancellationToken);
        Assert.Equal(2, third.RolloverCount);
        Assert.Equal(balanceAfterSecondTerm, third.Principal);
        Assert.Equal(new DateOnly(2026, 7, 15), third.StartDate);
        Assert.Equal(new DateOnly(2026, 10, 15), third.MaturityDate);
        Assert.Equal(5m, third.AnnualInterestRatePercent);

        // Warsaw 2026-10-20: settle the third term and pay the whole balance out.
        Factory.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero));
        var thirdPreview = await client.GetSettlementPreviewAsync(portfolioId, deposit.AssetId, cancellationToken);
        await client.SettleDepositAsync(
            portfolioId,
            deposit.AssetId,
            cancellationToken,
            NewSettleRequest(settledOn: new DateOnly(2026, 10, 15), grossInterest: thirdPreview.GrossInterest, tax: thirdPreview.Tax));
        var wholeBalance = (await client.GetAssetAsync(portfolioId, deposit.AssetId, cancellationToken)).Quantity;
        Assert.Equal(balanceAfterSecondTerm + thirdPreview.NetInterest, wholeBalance);

        var payOut = await client.PostAsJsonAsync(
            PayOutDepositUri(portfolioId, deposit.AssetId), NewPayOutRequest(cashId, new DateOnly(2026, 10, 16)), cancellationToken);

        Assert.True(payOut.IsSuccessStatusCode, $"The payout answered {(int)payOut.StatusCode}.");
        Assert.Equal(0m, (await client.GetAssetAsync(portfolioId, deposit.AssetId, cancellationToken)).Quantity);
        Assert.Equal(wholeBalance, (await client.GetAssetAsync(walletId, cashId, cancellationToken)).Quantity);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(portfolioId, deposit.AssetId, cancellationToken);
        var paidOut = await client.GetDepositAsync(portfolioId, deposit.AssetId, cancellationToken);
        Assert.Equal(DepositStatus.PaidOut, paidOut.Status);
        Assert.Equal(2, paidOut.RolloverCount);
        Assert.Equal(new DateOnly(2026, 10, 16), paidOut.PaidOutOn);

        // Opening + three net-interest credits + the payout's Withdraw.
        var transactions = (await client.ListTransactionsAsync(portfolioId, deposit.AssetId, cancellationToken)).Items;
        Assert.Equal(5, transactions.Count);
        Assert.Equal(4, transactions.Count(t => t.Type == TransactionType.Deposit));
        Assert.Equal(wholeBalance, Assert.Single(transactions, t => t.Type == TransactionType.Withdraw).Quantity);
    }
}
