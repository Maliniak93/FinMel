using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.Deposits.UpdateDeposit;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class UpdateDepositEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Update_PrincipalStartAndTerm_RewritesOpeningTransactionAndQuantity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        var openingId = Assert.Single((await client.ListTransactionsAsync(portfolioId, deposit.AssetId, cancellationToken)).Items).Id;
        var request = NewDepositRequest(
            name: "Renamed deposit",
            bankName: "Other bank",
            principal: 15_000m,
            startDate: new DateOnly(2026, 2, 1),
            termLength: 6,
            termUnit: DepositTermUnit.Months,
            annualInterestRatePercent: 5m,
            capitalization: DepositCapitalization.Quarterly,
            taxExempt: true,
            earlyBreakInterestLossPercent: 50m).ToUpdateRequest();

        var response = await client.PutAsJsonAsync(DepositUri(portfolioId, deposit.AssetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DepositResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(deposit.AssetId, body.AssetId);
        Assert.Equal("Renamed deposit", body.Name);
        Assert.Equal("Other bank", body.BankName);
        Assert.Equal("PLN", body.Currency);
        Assert.Equal(15_000m, body.Principal);
        Assert.Equal(new DateOnly(2026, 2, 1), body.StartDate);
        Assert.Equal(6, body.TermLength);
        Assert.Equal(new DateOnly(2026, 8, 1), body.MaturityDate);
        Assert.Equal(5m, body.AnnualInterestRatePercent);
        Assert.Equal(DepositCapitalization.Quarterly, body.Capitalization);
        Assert.True(body.TaxExempt);
        Assert.Equal(50m, body.EarlyBreakInterestLossPercent);
        Assert.Equal(0m, body.Projection.Tax);

        var asset = await client.GetAssetAsync(portfolioId, deposit.AssetId, cancellationToken);
        Assert.Equal(AssetClass.Deposit, asset.AssetClass);
        Assert.Equal("Renamed deposit", asset.Name);
        Assert.Equal(15_000m, asset.Quantity);
        Assert.Equal(1, asset.TransactionCount);
        Assert.Equal(new DateOnly(2026, 8, 1), asset.DepositMaturityDate);

        var opening = Assert.Single((await client.ListTransactionsAsync(portfolioId, deposit.AssetId, cancellationToken)).Items);
        Assert.Equal(openingId, opening.Id);
        Assert.Equal(TransactionType.Deposit, opening.Type);
        Assert.Equal(15_000m, opening.Quantity);
        Assert.Equal(1m, opening.UnitPrice);
        Assert.Equal(new DateOnly(2026, 2, 1), opening.Date);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(portfolioId, deposit.AssetId, cancellationToken);

        await using var dbContext = CreateDbContext(userId);
        var terms = await dbContext.Set<TermDeposit>().SingleAsync(t => t.AssetId == deposit.AssetId, cancellationToken);
        Assert.Equal(15_000m, terms.Principal);
        Assert.Equal(new DateOnly(2026, 8, 1), terms.MaturityDate);
    }

    [Fact]
    public async Task Update_FundedDeposit_RewritesBothLegs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateFundedDepositAsync(cancellationToken, cashBalance: 5_000m, principal: 1_000m);
        var request = NewDepositRequest(principal: 1_500m, startDate: new DateOnly(2026, 2, 1)).ToUpdateRequest();

        var response = await client.PutAsJsonAsync(
            DepositUri(funded.DepositPortfolioId, funded.Deposit.AssetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DepositResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(1_500m, body.Principal);
        Assert.Equal(funded.CashAssetId, body.FundingAssetId);
        Assert.Equal("Cash account", body.FundingAssetName);

        Assert.Equal(3_500m, (await client.GetAssetAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken)).Quantity);
        Assert.Equal(1_500m, (await client.GetAssetAsync(funded.DepositPortfolioId, funded.Deposit.AssetId, cancellationToken)).Quantity);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(funded.DepositPortfolioId, funded.Deposit.AssetId, cancellationToken);

        var withdraw = await client.GetCashWithdrawAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken);
        Assert.Equal(1_500m, withdraw.Quantity);
        Assert.Equal(new DateOnly(2026, 2, 1), withdraw.Date);
        Assert.Equal(1_500.00m, withdraw.ValuePln);
        var opening = Assert.Single((await client.ListTransactionsAsync(funded.DepositPortfolioId, funded.Deposit.AssetId, cancellationToken)).Items);
        Assert.Equal(1_500m, opening.Quantity);
        Assert.Equal(new DateOnly(2026, 2, 1), opening.Date);
        Assert.Equal(1_500.00m, opening.ValuePln);

        await using var dbContext = CreateDbContext(userId);
        var legs = await dbContext.Transactions.Where(t => t.TransferId != null).ToListAsync(cancellationToken);
        Assert.Equal(2, legs.Count);
        Assert.Single(legs.Select(t => t.TransferId).Distinct());
        Assert.Equal(TransactionType.Withdraw, Assert.Single(legs, t => t.AssetId == funded.CashAssetId).Type);
        Assert.Equal(TransactionType.Deposit, Assert.Single(legs, t => t.AssetId == funded.Deposit.AssetId).Type);
        Assert.All(legs, leg => Assert.Equal(1_500m, leg.Quantity));
        Assert.All(legs, leg => Assert.Equal(new DateOnly(2026, 2, 1), leg.Date));
    }

    [Fact]
    public async Task Update_FundedEurDepositNewStartDate_ReFreezesRateOnBothLegs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.FxRateLookupClient
            .WithRate("EUR", new DateOnly(2026, 1, 15), 4.20m)
            .WithRate("EUR", new DateOnly(2026, 2, 1), 4.30m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var walletId = await client.CreatePortfolioAsync(cancellationToken, name: "Wallet");
        var cashId = await client.AddCashAssetWithBalanceAsync(walletId, cancellationToken, currency: "EUR", name: "EUR cash");
        var savingsId = await client.CreatePortfolioAsync(cancellationToken, name: "Savings");
        var deposit = await client.AddDepositAsync(
            savingsId, cancellationToken, NewDepositRequest(currency: "EUR", principal: 1_000m, fundingAssetId: cashId));
        var request = NewDepositRequest(principal: 1_000m, startDate: new DateOnly(2026, 2, 1)).ToUpdateRequest();

        var response = await client.PutAsJsonAsync(DepositUri(savingsId, deposit.AssetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var withdraw = await client.GetCashWithdrawAsync(walletId, cashId, cancellationToken);
        Assert.Equal(4_300.00m, withdraw.ValuePln);
        var opening = Assert.Single((await client.ListTransactionsAsync(savingsId, deposit.AssetId, cancellationToken)).Items);
        Assert.Equal(4_300.00m, opening.ValuePln);
    }

    [Fact]
    public async Task Update_FundedDepositBeyondCashBalance_ReturnsInsufficientFunds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateFundedDepositAsync(cancellationToken, cashBalance: 5_000m, principal: 1_000m);
        // 5 000 in Cash: 1 000 already moved, so anything above 5 000 in total cannot be covered.
        var request = NewDepositRequest(principal: 5_000.01m).ToUpdateRequest();

        var response = await client.PutAsJsonAsync(
            DepositUri(funded.DepositPortfolioId, funded.Deposit.AssetId), request, cancellationToken);

        await response.AssertInsufficientFundsAsync(cancellationToken);
        await AssertFundedDepositUnchangedAsync(client, userId, funded, cancellationToken);
    }

    [Fact]
    public async Task Update_FundedDepositWithArchivedCashPortfolio_RejectsLegChangesOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateFundedDepositAsync(cancellationToken);
        await client.ArchivePortfolioAsync(funded.CashPortfolioId, cancellationToken);

        var legChange = await client.PutAsJsonAsync(
            DepositUri(funded.DepositPortfolioId, funded.Deposit.AssetId),
            NewDepositRequest(principal: 1_500m).ToUpdateRequest(),
            cancellationToken);

        await legChange.AssertPortfolioArchivedConflictAsync(cancellationToken);
        await AssertFundedDepositUnchangedAsync(client, userId, funded, cancellationToken);

        var rename = await client.PutAsJsonAsync(
            DepositUri(funded.DepositPortfolioId, funded.Deposit.AssetId),
            NewDepositRequest(name: "Renamed deposit", principal: 1_000m).ToUpdateRequest(),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        Assert.Equal("Renamed deposit", (await client.GetDepositAsync(funded.DepositPortfolioId, funded.Deposit.AssetId, cancellationToken)).Name);
    }

    [Fact]
    public async Task Update_ArchivedDeposit_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        await client.ArchiveAssetAsync(portfolioId, deposit.AssetId, cancellationToken);
        var request = NewDepositRequest(principal: 99_999m, name: "After archive").ToUpdateRequest();

        var response = await client.PutAsJsonAsync(DepositUri(portfolioId, deposit.AssetId), request, cancellationToken);

        await response.AssertAssetArchivedConflictAsync(cancellationToken);
        var unchanged = await client.GetDepositAsync(portfolioId, deposit.AssetId, cancellationToken);
        Assert.Equal("Term deposit", unchanged.Name);
        Assert.Equal(10_000m, unchanged.Principal);
        Assert.Equal(10_000m, (await client.GetAssetAsync(portfolioId, deposit.AssetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task Update_ArchivedFundingAsset_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateFundedDepositAsync(cancellationToken);
        await client.ArchiveAssetAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken);

        var response = await client.PutAsJsonAsync(
            DepositUri(funded.DepositPortfolioId, funded.Deposit.AssetId),
            NewDepositRequest(principal: 1_500m).ToUpdateRequest(),
            cancellationToken);

        await response.AssertAssetArchivedConflictAsync(cancellationToken);
        await AssertFundedDepositUnchangedAsync(client, userId, funded, cancellationToken);
    }

    [Fact]
    public async Task Update_ArchivedPortfolio_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        await client.ArchivePortfolioAsync(portfolioId, cancellationToken);
        var request = NewDepositRequest(principal: 99_999m, name: "After archive").ToUpdateRequest();

        var response = await client.PutAsJsonAsync(DepositUri(portfolioId, deposit.AssetId), request, cancellationToken);

        await response.AssertPortfolioArchivedConflictAsync(cancellationToken);
        var unchanged = await client.GetAsync(DepositUri(portfolioId, deposit.AssetId), cancellationToken);
        var body = await unchanged.Content.ReadFromJsonAsync<DepositResponse>(cancellationToken);
        Assert.Equal("Term deposit", body!.Name);
        Assert.Equal(10_000m, body.Principal);
        Assert.Equal(10_000m, (await client.GetAssetAsync(portfolioId, deposit.AssetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task Update_SettledDeposit_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        await client.SettleDepositAsync(portfolioId, deposit.AssetId, cancellationToken);
        var request = NewDepositRequest(name: "After settlement", principal: 20_000m).ToUpdateRequest();

        var response = await client.PutAsJsonAsync(DepositUri(portfolioId, deposit.AssetId), request, cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.DepositSettledErrorCode, cancellationToken);
        var unchanged = await client.GetDepositAsync(portfolioId, deposit.AssetId, cancellationToken);
        Assert.Equal("Term deposit", unchanged.Name);
        Assert.Equal(10_000m, unchanged.Principal);
        Assert.Equal(DepositStatus.Settled, unchanged.Status);
        Assert.Equal(147.95m, unchanged.SettledGrossInterest);
        var asset = await client.GetAssetAsync(portfolioId, deposit.AssetId, cancellationToken);
        Assert.Equal(10_119.83m, asset.Quantity);
        Assert.Equal(2, asset.TransactionCount);
        await using var dbContext = CreateDbContext(userId);
        Assert.Equal(10_000m, (await dbContext.Set<TermDeposit>().SingleAsync(t => t.AssetId == deposit.AssetId, cancellationToken)).Principal);
    }

    [Fact]
    public async Task Update_RolledOverDeposit_EditsTermsOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        var rolled = await client.RollOverDepositAsync(portfolioId, deposit.AssetId, cancellationToken);
        Assert.Equal(DepositStatus.Active, rolled.Status);
        var before = await SnapshotDepositAsync(client, userId, portfolioId, deposit.AssetId, cancellationToken);
        var request = NewDepositRequest(
            name: "Renamed deposit",
            bankName: "Other bank",
            principal: 10_119.83m,
            startDate: new DateOnly(2026, 4, 15),
            termLength: 6,
            termUnit: DepositTermUnit.Months,
            annualInterestRatePercent: 4.5m,
            capitalization: DepositCapitalization.Quarterly,
            taxExempt: true,
            earlyBreakInterestLossPercent: 50m).ToUpdateRequest();

        var response = await client.PutAsJsonAsync(DepositUri(portfolioId, deposit.AssetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DepositResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal("Renamed deposit", body.Name);
        Assert.Equal("Other bank", body.BankName);
        Assert.Equal(10_119.83m, body.Principal);
        Assert.Equal(new DateOnly(2026, 4, 15), body.StartDate);
        Assert.Equal(6, body.TermLength);
        Assert.Equal(new DateOnly(2026, 10, 15), body.MaturityDate);
        Assert.Equal(4.5m, body.AnnualInterestRatePercent);
        Assert.Equal(DepositCapitalization.Quarterly, body.Capitalization);
        Assert.True(body.TaxExempt);
        Assert.Equal(50m, body.EarlyBreakInterestLossPercent);
        Assert.Equal(1, body.RolloverCount);
        Assert.Equal(DepositStatus.Active, body.Status);

        var after = await SnapshotDepositAsync(client, userId, portfolioId, deposit.AssetId, cancellationToken);
        Assert.Equal(before.Rows, after.Rows);
        Assert.Equal(before.Transactions, after.Transactions);
        var asset = await client.GetAssetAsync(portfolioId, deposit.AssetId, cancellationToken);
        Assert.Equal(10_119.83m, asset.Quantity);
        Assert.Equal(2, asset.TransactionCount);
        Assert.Equal("Renamed deposit", asset.Name);
        Assert.Equal(new DateOnly(2026, 10, 15), asset.DepositMaturityDate);

        await using var dbContext = CreateDbContext(userId);
        var terms = await dbContext.Set<TermDeposit>().SingleAsync(t => t.AssetId == deposit.AssetId, cancellationToken);
        Assert.Equal(1, terms.RolloverCount);
        Assert.Equal(new DateOnly(2026, 10, 15), terms.MaturityDate);
    }

    [Theory]
    [InlineData("principal")]
    [InlineData("start-date")]
    [InlineData("both")]
    public async Task Update_RolledOverDepositPrincipalOrStart_ReturnsConflict(string changedField)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        await client.RollOverDepositAsync(portfolioId, deposit.AssetId, cancellationToken);
        var before = await SnapshotDepositAsync(client, userId, portfolioId, deposit.AssetId, cancellationToken);
        var request = changedField switch
        {
            "principal" => NewDepositRequest(name: "Renamed", principal: 10_000m, startDate: new DateOnly(2026, 4, 15)),
            "start-date" => NewDepositRequest(name: "Renamed", principal: 10_119.83m, startDate: new DateOnly(2026, 4, 16)),
            "both" => NewDepositRequest(name: "Renamed", principal: 12_000m, startDate: new DateOnly(2026, 1, 15)),
            _ => throw new ArgumentOutOfRangeException(nameof(changedField), changedField, null)
        };

        var response = await client.PutAsJsonAsync(DepositUri(portfolioId, deposit.AssetId), request.ToUpdateRequest(), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.DepositRolledOverErrorCode, cancellationToken);
        await AssertDepositUnchangedAsync(client, userId, portfolioId, deposit.AssetId, before, cancellationToken);
        Assert.Equal("Term deposit", before.Deposit.Name);
        Assert.Equal(1, before.Deposit.RolloverCount);
    }

    [Fact]
    public async Task Update_SettledRolledOverDeposit_ReturnsSettledConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        await client.RollOverDepositAsync(portfolioId, deposit.AssetId, cancellationToken);
        // Warsaw 2026-07-20 — past the rolled-over term's 2026-07-15 maturity.
        Factory.Clock.SetUtcNow(new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero));
        var preview = await client.GetSettlementPreviewAsync(portfolioId, deposit.AssetId, cancellationToken);
        await client.SettleDepositAsync(
            portfolioId,
            deposit.AssetId,
            cancellationToken,
            NewSettleRequest(settledOn: new DateOnly(2026, 7, 15), grossInterest: preview.GrossInterest, tax: preview.Tax));
        var before = await SnapshotDepositAsync(client, userId, portfolioId, deposit.AssetId, cancellationToken);
        var request = NewDepositRequest(name: "After settlement", principal: 10_119.83m, startDate: new DateOnly(2026, 4, 15)).ToUpdateRequest();

        var response = await client.PutAsJsonAsync(DepositUri(portfolioId, deposit.AssetId), request, cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.DepositSettledErrorCode, cancellationToken);
        await AssertDepositUnchangedAsync(client, userId, portfolioId, deposit.AssetId, before, cancellationToken);
    }

    [Theory]
    [InlineData("principal-zero", nameof(UpdateDepositRequest.Principal))]
    [InlineData("rate-over-100", nameof(UpdateDepositRequest.AnnualInterestRatePercent))]
    [InlineData("term-zero", nameof(UpdateDepositRequest.TermLength))]
    [InlineData("name-empty", nameof(UpdateDepositRequest.Name))]
    public async Task Update_InvalidTerms_ReturnsBadRequestAndChangesNothing(string invalidCase, string field)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        var valid = NewDepositRequest().ToUpdateRequest();
        var request = invalidCase switch
        {
            "principal-zero" => valid with { Principal = 0m },
            "rate-over-100" => valid with { AnnualInterestRatePercent = 100.01m },
            "term-zero" => valid with { TermLength = 0 },
            "name-empty" => valid with { Name = "" },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase), invalidCase, null)
        };

        var response = await client.PutAsJsonAsync(DepositUri(portfolioId, deposit.AssetId), request, cancellationToken);

        await response.AssertFieldValidationErrorAsync(field, cancellationToken);
        var asset = await client.GetAssetAsync(portfolioId, deposit.AssetId, cancellationToken);
        Assert.Equal(10_000m, asset.Quantity);
        Assert.Equal(new DateOnly(2026, 4, 15), asset.DepositMaturityDate);
    }

    [Fact]
    public async Task Update_NonDepositAsset_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        // A real deposit next to it: the route exists and answers for deposits, just not for this asset.
        var (portfolioId, _) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        var cashId = await client.AddCashAssetAsync(portfolioId, cancellationToken);

        var response = await client.PutAsJsonAsync(
            DepositUri(portfolioId, cashId), NewDepositRequest().ToUpdateRequest(), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var cash = await client.GetAssetAsync(portfolioId, cashId, cancellationToken);
        Assert.Equal(AssetClass.Cash, cash.AssetClass);
        Assert.Equal(0m, cash.Quantity);
    }

    [Fact]
    public async Task Update_NonExistentDeposit_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, _) = await client.CreatePortfolioWithDepositAsync(cancellationToken);

        var response = await client.PutAsJsonAsync(
            DepositUri(portfolioId, Guid.NewGuid()), NewDepositRequest().ToUpdateRequest(), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
