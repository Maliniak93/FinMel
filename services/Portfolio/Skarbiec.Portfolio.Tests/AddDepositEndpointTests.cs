using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.Deposits.AddDeposit;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class AddDepositEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    private static readonly DateOnly MBankOpening = new(2026, 7, 1);

    private static readonly DateOnly DepositOpening = new(2026, 10, 1);

    [Fact]
    public async Task FromBankAccount_DebitsSource()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var mBank = await client.CreateBankAccountAsync(cancellationToken, "mBank", balance: 20_000m, depositedOn: MBankOpening);

        var response = await client.PostAsJsonAsync(
            AllDepositsUri,
            NewDepositRequest(principal: 10_000m, startDate: DepositOpening, termLength: 6, sourceAccountId: mBank.AccountId),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DepositResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(DepositUri(body.AssetId), response.Headers.Location?.OriginalString);
        Assert.Equal("PLN", body.Currency);
        Assert.Equal(10_000m, body.Principal);
        Assert.Equal(mBank.AccountId, body.SourceAccountId);
        Assert.Equal("mBank", body.SourceAccountName);
        Assert.Equal(mBank.AccountId, body.ReturnAccountId);
        Assert.Equal("mBank", body.ReturnAccountName);

        Assert.Equal(10_000m, await client.GetCashBalanceAsync(mBank.AccountId, cancellationToken));
        var asset = await client.GetAssetAsync(body.AssetId, cancellationToken);
        Assert.Equal(AssetClass.Deposit, asset.AssetClass);
        Assert.Equal("PLN", asset.Currency);
        Assert.Equal(10_000m, asset.Quantity);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(mBank.MoneyAssetId, cancellationToken);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(body.AssetId, cancellationToken);

        var withdraw = await client.GetCashWithdrawAsync(mBank.MoneyAssetId, cancellationToken);
        Assert.Equal(10_000m, withdraw.Quantity);
        Assert.Equal(DepositOpening, withdraw.Date);
        var opening = Assert.Single((await client.ListTransactionsAsync(body.AssetId, cancellationToken)).Items);
        Assert.Equal(TransactionType.Deposit, opening.Type);
        Assert.Equal(10_000m, opening.Quantity);
        Assert.Equal(DepositOpening, opening.Date);

        await using var dbContext = CreateDbContext(userId);
        var legs = await dbContext.Transactions.Where(t => t.TransferId != null).ToListAsync(cancellationToken);
        Assert.Equal(2, legs.Count);
        Assert.Single(legs.Select(t => t.TransferId).Distinct());
        Assert.Equal(withdraw.Id, Assert.Single(legs, t => t.AssetId == mBank.MoneyAssetId).Id);
        Assert.Equal(opening.Id, Assert.Single(legs, t => t.AssetId == body.AssetId).Id);
        var terms = await dbContext.Set<TermDeposit>().SingleAsync(t => t.AssetId == body.AssetId, cancellationToken);
        Assert.Equal(mBank.AccountId, terms.SourceAccountId);
        Assert.Equal(mBank.AccountId, terms.ReturnAccountId);
    }

    [Fact]
    public async Task SourceRules()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var mBank = await client.CreateBankAccountAsync(cancellationToken, "mBank", balance: 20_000m, depositedOn: MBankOpening);
        var brokerageId = await client.CreateAccountAsync(cancellationToken, "XTB");
        var oldBank = await client.CreateBankAccountAsync(cancellationToken, "Old bank");
        await client.ArchiveAccountAsync(oldBank.AccountId, cancellationToken);
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var tooMuch = await client.PostAsJsonAsync(
            AllDepositsUri,
            NewDepositRequest(principal: 30_000m, startDate: DepositOpening, sourceAccountId: mBank.AccountId),
            cancellationToken);
        var brokerage = await client.PostAsJsonAsync(
            AllDepositsUri,
            NewDepositRequest(startDate: DepositOpening, sourceAccountId: brokerageId),
            cancellationToken);
        var unknown = await client.PostAsJsonAsync(
            AllDepositsUri,
            NewDepositRequest(startDate: DepositOpening, sourceAccountId: Guid.NewGuid()),
            cancellationToken);
        var archived = await client.PostAsJsonAsync(
            AllDepositsUri,
            NewDepositRequest(startDate: DepositOpening, sourceAccountId: oldBank.AccountId),
            cancellationToken);

        await tooMuch.AssertInsufficientFundsAsync(cancellationToken);
        await brokerage.AssertProblemAsync(HttpStatusCode.BadRequest, PortfolioAssertions.BankAccountRequiredErrorCode, cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        await archived.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.AccountArchivedErrorCode, cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
        Assert.Equal(20_000m, await client.GetCashBalanceAsync(mBank.AccountId, cancellationToken));
        await using var dbContext = CreateDbContext(userId);
        Assert.False(await dbContext.Transactions.AnyAsync(t => t.TransferId != null, cancellationToken));
    }

    [Fact]
    public async Task CurrencyFromSource()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var usd = await client.CreateBankAccountAsync(
            cancellationToken, "mBank USD", currency: "USD", balance: 1_000m, depositedOn: MBankOpening);
        var pln = await client.CreateBankAccountAsync(cancellationToken, "mBank");
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var mismatch = await client.PostAsJsonAsync(
            AllDepositsUri,
            NewDepositRequest(principal: 500m, startDate: DepositOpening, sourceAccountId: usd.AccountId, returnAccountId: pln.AccountId),
            cancellationToken);

        await mismatch.AssertProblemAsync(HttpStatusCode.BadRequest, TransferCurrencyMismatchErrorCode, cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));

        var response = await client.PostAsJsonAsync(
            AllDepositsUri,
            NewDepositRequest(principal: 500m, startDate: DepositOpening, sourceAccountId: usd.AccountId),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DepositResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal("USD", body.Currency);
        Assert.Equal("USD", (await client.GetAssetAsync(body.AssetId, cancellationToken)).Currency);
        Assert.Equal(500m, await client.GetCashBalanceAsync(usd.AccountId, cancellationToken));
    }

    [Fact]
    public async Task Add_WithReturnAccount_StoresItWithoutTouchingIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var mBank = await client.CreateBankAccountAsync(cancellationToken, "mBank", balance: 20_000m, depositedOn: MBankOpening);
        var nest = await client.CreateBankAccountAsync(cancellationToken, "Nest", SavingsVariant);

        var response = await client.PostAsJsonAsync(
            AllDepositsUri,
            NewDepositRequest(startDate: DepositOpening, sourceAccountId: mBank.AccountId, returnAccountId: nest.AccountId),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DepositResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(mBank.AccountId, body.SourceAccountId);
        Assert.Equal(nest.AccountId, body.ReturnAccountId);
        Assert.Equal("Nest", body.ReturnAccountName);
        Assert.Equal(10_000m, await client.GetCashBalanceAsync(mBank.AccountId, cancellationToken));
        Assert.Equal(0m, await client.GetCashBalanceAsync(nest.AccountId, cancellationToken));
    }

    [Fact]
    public async Task Add_DatedBeforeSourceTopUp_ReturnsInsufficientFunds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var mBank = await client.CreateBankAccountAsync(
            cancellationToken, "mBank", balance: 5_000m, depositedOn: new DateOnly(2026, 2, 1));

        var response = await client.PostAsJsonAsync(
            AllDepositsUri,
            NewDepositRequest(principal: 1_000m, startDate: new DateOnly(2026, 1, 15), sourceAccountId: mBank.AccountId),
            cancellationToken);

        await response.AssertInsufficientFundsAsync(cancellationToken);
        Assert.Equal(5_000m, await client.GetCashBalanceAsync(mBank.AccountId, cancellationToken));
        await using var dbContext = CreateDbContext(userId);
        Assert.Equal(0, await dbContext.Set<TermDeposit>().CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Add_ValidDeposit_ReturnsCreatedWithPrincipalAsQuantity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero));
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var request = await client.WithFundingAsync(NewDepositRequest(), cancellationToken);

        var response = await client.PostAsJsonAsync(AllDepositsUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DepositResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal("Term deposit", body.Name);
        Assert.Equal("Test bank", body.BankName);
        Assert.Equal("PLN", body.Currency);
        Assert.Equal(10_000m, body.Principal);
        Assert.Equal(new DateOnly(2026, 1, 15), body.StartDate);
        Assert.Equal(3, body.TermLength);
        Assert.Equal(DepositTermUnit.Months, body.TermUnit);
        Assert.Equal(new DateOnly(2026, 4, 15), body.MaturityDate);
        Assert.Equal(6m, body.AnnualInterestRatePercent);
        Assert.Equal(DepositCapitalization.AtMaturity, body.Capitalization);
        Assert.Equal(100m, body.EarlyBreakInterestLossPercent);
        Assert.Equal(147.95m, body.Projection.GrossInterest);
        Assert.Equal(28.12m, body.Projection.Tax);
        Assert.Equal(119.83m, body.Projection.NetInterest);
        Assert.Equal(10_119.83m, body.Projection.FinalAmount);
        Assert.Equal(DepositStatus.Active, body.Status);

        var asset = await client.GetAssetAsync(body.AssetId, cancellationToken);
        Assert.Equal(AssetClass.Deposit, asset.AssetClass);
        Assert.Equal(AssetValuationMode.CurrencyValued, asset.ValuationMode);
        Assert.Equal(10_000m, asset.Quantity);
        Assert.Equal(1, asset.TransactionCount);
        Assert.Equal(new DateOnly(2026, 4, 15), asset.DepositMaturityDate);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(body.AssetId, cancellationToken);

        await using var dbContext = CreateDbContext(userId);
        var terms = await dbContext.Set<TermDeposit>().SingleAsync(t => t.AssetId == body.AssetId, cancellationToken);
        Assert.Equal(userId, terms.UserId);
        Assert.Equal(10_000m, terms.Principal);
        Assert.Equal(new DateOnly(2026, 4, 15), terms.MaturityDate);
    }

    [Fact]
    public async Task Add_EurSource_FreezesStartDateFxRateOnOpeningTransaction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.FxRateLookupClient.WithRate("EUR", new DateOnly(2026, 1, 15), 4.25m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var eur = await client.CreateBankAccountAsync(
            cancellationToken, "mBank EUR", currency: "EUR", balance: 50_000m, depositedOn: DefaultTopUpDate);

        var response = await client.PostAsJsonAsync(
            AllDepositsUri, NewDepositRequest(sourceAccountId: eur.AccountId), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DepositResponse>(cancellationToken);
        Assert.Equal("EUR", body!.Currency);
        var opening = Assert.Single((await client.ListTransactionsAsync(body.AssetId, cancellationToken)).Items);
        Assert.Equal("EUR", opening.Currency);
        Assert.Equal(42_500.00m, opening.ValuePln);
    }

    [Theory]
    [InlineData("principal-zero", nameof(AddDepositRequest.Principal))]
    [InlineData("principal-negative", nameof(AddDepositRequest.Principal))]
    [InlineData("rate-negative", nameof(AddDepositRequest.AnnualInterestRatePercent))]
    [InlineData("rate-over-100", nameof(AddDepositRequest.AnnualInterestRatePercent))]
    [InlineData("term-zero", nameof(AddDepositRequest.TermLength))]
    [InlineData("term-over-3650-days", nameof(AddDepositRequest.TermLength))]
    [InlineData("term-over-120-months", nameof(AddDepositRequest.TermLength))]
    [InlineData("loss-negative", nameof(AddDepositRequest.EarlyBreakInterestLossPercent))]
    [InlineData("loss-over-100", nameof(AddDepositRequest.EarlyBreakInterestLossPercent))]
    [InlineData("name-empty", nameof(AddDepositRequest.Name))]
    [InlineData("bank-name-too-long", nameof(AddDepositRequest.BankName))]
    public async Task Add_InvalidTerms_ReturnsBadRequest(string invalidCase, string field)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var valid = await client.WithFundingAsync(NewDepositRequest(), cancellationToken);
        var request = invalidCase switch
        {
            "principal-zero" => valid with { Principal = 0m },
            "principal-negative" => valid with { Principal = -100m },
            "rate-negative" => valid with { AnnualInterestRatePercent = -0.01m },
            "rate-over-100" => valid with { AnnualInterestRatePercent = 100.01m },
            "term-zero" => valid with { TermLength = 0 },
            "term-over-3650-days" => valid with { TermLength = 3651, TermUnit = DepositTermUnit.Days },
            "term-over-120-months" => valid with { TermLength = 121, TermUnit = DepositTermUnit.Months },
            "loss-negative" => valid with { EarlyBreakInterestLossPercent = -1m },
            "loss-over-100" => valid with { EarlyBreakInterestLossPercent = 100.01m },
            "name-empty" => valid with { Name = "" },
            "bank-name-too-long" => valid with { BankName = new string('B', 101) },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase), invalidCase, null)
        };
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.PostAsJsonAsync(AllDepositsUri, request, cancellationToken);

        await response.AssertFieldValidationErrorAsync(field, cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
    }

    [Theory]
    [InlineData("rate-zero")]
    [InlineData("rate-100")]
    [InlineData("term-3650-days")]
    [InlineData("term-120-months")]
    [InlineData("loss-zero")]
    [InlineData("no-bank-name")]
    public async Task Add_BoundaryTerms_ReturnsCreated(string boundaryCase)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var valid = await client.WithFundingAsync(NewDepositRequest(), cancellationToken);
        var request = boundaryCase switch
        {
            "rate-zero" => valid with { AnnualInterestRatePercent = 0m },
            "rate-100" => valid with { AnnualInterestRatePercent = 100m },
            "term-3650-days" => valid with { TermLength = 3650, TermUnit = DepositTermUnit.Days },
            "term-120-months" => valid with { TermLength = 120, TermUnit = DepositTermUnit.Months },
            "loss-zero" => valid with { EarlyBreakInterestLossPercent = 0m },
            "no-bank-name" => valid with { BankName = null },
            _ => throw new ArgumentOutOfRangeException(nameof(boundaryCase), boundaryCase, null)
        };

        var response = await client.PostAsJsonAsync(AllDepositsUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Add_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            AllDepositsUri, NewDepositRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
