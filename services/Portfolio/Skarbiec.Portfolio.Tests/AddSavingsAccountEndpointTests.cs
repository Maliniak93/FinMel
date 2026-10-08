using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.SavingsAccounts;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class AddSavingsAccountEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Add_WithOpeningDeposit_CreatesAccount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken, name: "Savings");

        var response = await client.PostAsJsonAsync(SavingsAccountsUri(portfolioId), NewSavingsAccountRequest(), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SavingsAccountResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(SavingsAccountUri(portfolioId, body.AssetId), response.Headers.Location?.OriginalString);
        Assert.Equal(portfolioId, body.PortfolioId);
        Assert.Equal("Savings", body.PortfolioName);
        Assert.False(body.PortfolioIsArchived);
        Assert.False(body.IsArchived);
        Assert.Equal("Savings account", body.Name);
        Assert.Equal("Test bank", body.BankName);
        Assert.Equal("PLN", body.Currency);
        Assert.Equal(10_000m, body.Balance);
        Assert.Equal(5.25m, body.AnnualInterestRatePercent);
        Assert.False(body.TaxExempt);

        var asset = await client.GetAssetAsync(portfolioId, body.AssetId, cancellationToken);
        Assert.Equal(AssetClass.Savings, asset.AssetClass);
        Assert.Equal(AssetValuationMode.CurrencyValued, asset.ValuationMode);
        Assert.Equal(10_000m, asset.Quantity);
        Assert.Equal(1, asset.TransactionCount);

        var opening = Assert.Single((await client.ListTransactionsAsync(portfolioId, body.AssetId, cancellationToken)).Items);
        Assert.Equal(TransactionType.Deposit, opening.Type);
        Assert.Equal(10_000m, opening.Quantity);
        Assert.Equal(1m, opening.UnitPrice);
        Assert.Equal(SavingsToday, opening.Date);
        Assert.Equal(10_000.00m, opening.ValuePln);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(portfolioId, body.AssetId, cancellationToken);

        await using var dbContext = CreateDbContext(userId);
        var terms = await dbContext.Set<SavingsAccount>().SingleAsync(t => t.AssetId == body.AssetId, cancellationToken);
        Assert.Equal(userId, terms.UserId);
        Assert.Equal("Test bank", terms.BankName);
        Assert.Equal(5.25m, terms.AnnualInterestRatePercent);
        Assert.False(terms.TaxExempt);
    }

    [Fact]
    public async Task Add_WithoutOpeningDeposit_StartsEmpty()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);

        var response = await client.PostAsJsonAsync(
            SavingsAccountsUri(portfolioId), NewSavingsAccountRequest(withOpeningDeposit: false), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SavingsAccountResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(0m, body.Balance);
        var asset = await client.GetAssetAsync(portfolioId, body.AssetId, cancellationToken);
        Assert.Equal(AssetClass.Savings, asset.AssetClass);
        Assert.Equal(0m, asset.Quantity);
        Assert.Equal(0, asset.TransactionCount);
        Assert.Empty((await client.ListTransactionsAsync(portfolioId, body.AssetId, cancellationToken)).Items);
    }

    [Fact]
    public async Task Add_EurAccountWithOpeningDeposit_FreezesFxRate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        Factory.FxRateLookupClient.WithRate("EUR", SavingsToday, 4.25m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);

        var response = await client.PostAsJsonAsync(
            SavingsAccountsUri(portfolioId), NewSavingsAccountRequest(currency: "EUR", openingAmount: 1_000m), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SavingsAccountResponse>(cancellationToken);
        var opening = Assert.Single((await client.ListTransactionsAsync(portfolioId, body!.AssetId, cancellationToken)).Items);
        Assert.Equal("EUR", opening.Currency);
        Assert.Equal(4_250.00m, opening.ValuePln);
    }

    [Theory]
    [InlineData("name-empty")]
    [InlineData("rate-negative")]
    [InlineData("rate-over-100")]
    [InlineData("currency-unsupported")]
    [InlineData("opening-amount-zero")]
    [InlineData("opening-date-tomorrow")]
    public async Task Add_InvalidInput_ReturnsBadRequest(string invalidCase)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var request = invalidCase switch
        {
            "name-empty" => NewSavingsAccountRequest(name: ""),
            "rate-negative" => NewSavingsAccountRequest(annualInterestRatePercent: -1m),
            "rate-over-100" => NewSavingsAccountRequest(annualInterestRatePercent: 101m),
            "currency-unsupported" => NewSavingsAccountRequest(currency: "XYZ"),
            "opening-amount-zero" => NewSavingsAccountRequest(openingAmount: 0m),
            "opening-date-tomorrow" => NewSavingsAccountRequest(openingDate: SavingsToday.AddDays(1)),
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase), invalidCase, null)
        };

        var response = await client.PostAsJsonAsync(SavingsAccountsUri(portfolioId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var dbContext = CreateDbContext(userId);
        Assert.Equal(0, await dbContext.Assets.CountAsync(cancellationToken));
        Assert.Equal(0, await dbContext.Transactions.CountAsync(cancellationToken));
        Assert.Equal(0, await dbContext.Set<SavingsAccount>().CountAsync(cancellationToken));
    }

    [Theory]
    [InlineData("rate-zero")]
    [InlineData("rate-100")]
    [InlineData("opening-date-today")]
    public async Task Add_BoundaryInput_ReturnsCreated(string boundaryCase)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var request = boundaryCase switch
        {
            "rate-zero" => NewSavingsAccountRequest(annualInterestRatePercent: 0m),
            "rate-100" => NewSavingsAccountRequest(annualInterestRatePercent: 100m),
            "opening-date-today" => NewSavingsAccountRequest(openingDate: SavingsToday),
            _ => throw new ArgumentOutOfRangeException(nameof(boundaryCase), boundaryCase, null)
        };

        var response = await client.PostAsJsonAsync(SavingsAccountsUri(portfolioId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Add_ArchivedPortfolio_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        await client.ArchivePortfolioAsync(portfolioId, cancellationToken);

        var response = await client.PostAsJsonAsync(SavingsAccountsUri(portfolioId), NewSavingsAccountRequest(), cancellationToken);

        await response.AssertPortfolioArchivedConflictAsync(cancellationToken);
        await using var dbContext = CreateDbContext(userId);
        Assert.Equal(0, await dbContext.Assets.CountAsync(cancellationToken));
        Assert.Equal(0, await dbContext.Transactions.CountAsync(cancellationToken));
        Assert.Equal(0, await dbContext.Set<SavingsAccount>().CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Add_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            SavingsAccountsUri(Guid.NewGuid()), NewSavingsAccountRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
