using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.Deposits.AddDeposit;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

/// <summary>
/// term-deposits: <c>POST /api/portfolio/portfolios/{portfolioId}/deposits</c> creates the Deposit-class
/// asset, its system-managed opening Deposit transaction and its <see cref="TermDeposit"/> terms
/// together, and answers with the terms, maturity date, projection and status.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class AddDepositEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    /// <summary>AC-5 (HTTP half; the event is proven by <see cref="PortfolioOutboxTests.AddDeposit_PublishesPositionChangedWithPrincipal"/>).</summary>
    [Fact]
    public async Task Add_ValidDeposit_ReturnsCreatedWithPrincipalAsQuantity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero));
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken, name: "Savings");

        var response = await client.PostAsJsonAsync(DepositsUri(portfolioId), NewDepositRequest(), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DepositResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(DepositUri(portfolioId, body.AssetId), response.Headers.Location?.OriginalString);
        Assert.Equal(portfolioId, body.PortfolioId);
        Assert.Equal("Savings", body.PortfolioName);
        Assert.False(body.PortfolioIsArchived);
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
        Assert.False(body.TaxExempt);
        Assert.Equal(100m, body.EarlyBreakInterestLossPercent);
        Assert.Equal(147.95m, body.Projection.GrossInterest);
        Assert.Equal(28.12m, body.Projection.Tax);
        Assert.Equal(119.83m, body.Projection.NetInterest);
        Assert.Equal(10_119.83m, body.Projection.FinalAmount);
        Assert.Equal(DepositStatus.Active, body.Status);

        var asset = await client.GetAssetAsync(portfolioId, body.AssetId, cancellationToken);
        Assert.Equal(AssetClass.Deposit, asset.AssetClass);
        Assert.Equal(AssetValuationMode.CurrencyValued, asset.ValuationMode);
        Assert.Equal(10_000m, asset.Quantity);
        Assert.Equal(1, asset.TransactionCount);
        Assert.Equal(new DateOnly(2026, 4, 15), asset.DepositMaturityDate);

        var opening = Assert.Single((await client.ListTransactionsAsync(portfolioId, body.AssetId, cancellationToken)).Items);
        Assert.Equal(TransactionType.Deposit, opening.Type);
        Assert.Equal(10_000m, opening.Quantity);
        Assert.Equal(1m, opening.UnitPrice);
        Assert.Equal(new DateOnly(2026, 1, 15), opening.Date);
        Assert.Equal(10_000.00m, opening.ValuePln);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(portfolioId, body.AssetId, cancellationToken);

        await using var dbContext = CreateDbContext(userId);
        var terms = await dbContext.Set<TermDeposit>().SingleAsync(t => t.AssetId == body.AssetId, cancellationToken);
        Assert.Equal(userId, terms.UserId);
        Assert.Equal(10_000m, terms.Principal);
        Assert.Equal(new DateOnly(2026, 4, 15), terms.MaturityDate);
    }

    /// <summary>The opening transaction freezes the start-date PLN rate like any transaction write (ADR-026).</summary>
    [Fact]
    public async Task Add_EurDeposit_FreezesStartDateFxRateOnOpeningTransaction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.FxRateLookupClient.WithRate("EUR", new DateOnly(2026, 1, 15), 4.25m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);

        var response = await client.PostAsJsonAsync(
            DepositsUri(portfolioId), NewDepositRequest(currency: "EUR"), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DepositResponse>(cancellationToken);
        Assert.Equal("EUR", body!.Currency);
        var opening = Assert.Single((await client.ListTransactionsAsync(portfolioId, body.AssetId, cancellationToken)).Items);
        Assert.Equal("EUR", opening.Currency);
        Assert.Equal(42_500.00m, opening.ValuePln);
        Assert.Equal(("EUR", new DateOnly(2026, 1, 15)), Assert.Single(Factory.FxRateLookupClient.Calls));
    }

    /// <summary>AC-6: every invalid term is a 400 keyed on the offending field, and nothing is written.</summary>
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
    [InlineData("currency-unsupported", nameof(AddDepositRequest.Currency))]
    [InlineData("name-empty", nameof(AddDepositRequest.Name))]
    [InlineData("bank-name-too-long", nameof(AddDepositRequest.BankName))]
    public async Task Add_InvalidTerms_ReturnsBadRequest(string invalidCase, string field)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var valid = NewDepositRequest();
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
            "currency-unsupported" => valid with { Currency = "CHF" },
            "name-empty" => valid with { Name = "" },
            "bank-name-too-long" => valid with { BankName = new string('B', 101) },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase), invalidCase, null)
        };

        var response = await client.PostAsJsonAsync(DepositsUri(portfolioId), request, cancellationToken);

        await response.AssertFieldValidationErrorAsync(field, cancellationToken);
        await using var dbContext = CreateDbContext(userId);
        Assert.Equal(0, await dbContext.Assets.CountAsync(cancellationToken));
        Assert.Equal(0, await dbContext.Transactions.CountAsync(cancellationToken));
        Assert.Equal(0, await dbContext.Set<TermDeposit>().CountAsync(cancellationToken));
    }

    /// <summary>AC-6 boundaries: the edges of every range are themselves valid.</summary>
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
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var valid = NewDepositRequest();
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

        var response = await client.PostAsJsonAsync(DepositsUri(portfolioId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>AC-7: an archived portfolio is read-only — 409 and nothing is written.</summary>
    [Fact]
    public async Task Add_ArchivedPortfolio_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        await client.ArchivePortfolioAsync(portfolioId, cancellationToken);

        var response = await client.PostAsJsonAsync(DepositsUri(portfolioId), NewDepositRequest(), cancellationToken);

        await response.AssertPortfolioArchivedConflictAsync(cancellationToken);
        await using var dbContext = CreateDbContext(userId);
        Assert.Equal(0, await dbContext.Assets.CountAsync(cancellationToken));
        Assert.Equal(0, await dbContext.Transactions.CountAsync(cancellationToken));
        Assert.Equal(0, await dbContext.Set<TermDeposit>().CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Add_ForNonExistentPortfolio_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        // Control: the same request into a real portfolio is accepted.
        await client.CreatePortfolioWithDepositAsync(cancellationToken);

        var response = await client.PostAsJsonAsync(DepositsUri(Guid.NewGuid()), NewDepositRequest(), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Add_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            DepositsUri(Guid.NewGuid()), NewDepositRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
