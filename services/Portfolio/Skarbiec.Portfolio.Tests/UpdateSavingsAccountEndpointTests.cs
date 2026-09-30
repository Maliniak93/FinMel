using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.SavingsAccounts;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

/// <summary>
/// savings-accounts: <c>PUT .../savings-accounts/{assetId}</c> edits the name, bank, rate and tax
/// status. Currency and balance are not in the request, and nothing is published.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class UpdateSavingsAccountEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    /// <summary>AC-4.</summary>
    [Fact]
    public async Task Update_ChangesTermsAndKeepsBalance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken);

        var response = await client.PutAsJsonAsync(
            SavingsAccountUri(portfolioId, account.AssetId), NewUpdateSavingsAccountRequest(), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SavingsAccountResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(account.AssetId, body.AssetId);
        Assert.Equal("Renamed savings", body.Name);
        Assert.Equal("Other bank", body.BankName);
        Assert.Equal("PLN", body.Currency);
        Assert.Equal(3m, body.AnnualInterestRatePercent);
        Assert.True(body.TaxExempt);
        Assert.Equal(10_000m, body.Balance);

        var fetched = await client.GetSavingsAccountAsync(portfolioId, account.AssetId, cancellationToken);
        Assert.Equal(body, fetched);
        var asset = await client.GetAssetAsync(portfolioId, account.AssetId, cancellationToken);
        Assert.Equal("Renamed savings", asset.Name);
        Assert.Equal(10_000m, asset.Quantity);
        Assert.Equal(1, asset.TransactionCount);

        await using var dbContext = CreateDbContext(userId);
        var terms = await dbContext.Set<SavingsAccount>().SingleAsync(t => t.AssetId == account.AssetId, cancellationToken);
        Assert.Equal("Other bank", terms.BankName);
        Assert.Equal(3m, terms.AnnualInterestRatePercent);
        Assert.True(terms.TaxExempt);
    }

    /// <summary>AC-4: a term deposit's id or an unknown id is a 404 — the endpoints address savings accounts only.</summary>
    [Fact]
    public async Task Update_NotASavingsAccount_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        var cashId = await client.AddCashAssetAsync(portfolioId, cancellationToken);
        var payload = NewUpdateSavingsAccountRequest();

        var forDeposit = await client.PutAsJsonAsync(SavingsAccountUri(portfolioId, deposit.AssetId), payload, cancellationToken);
        var forCash = await client.PutAsJsonAsync(SavingsAccountUri(portfolioId, cashId), payload, cancellationToken);
        var forUnknown = await client.PutAsJsonAsync(SavingsAccountUri(portfolioId, Guid.NewGuid()), payload, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, forDeposit.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, forCash.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, forUnknown.StatusCode);
        Assert.Equal("Term deposit", (await client.GetAssetAsync(portfolioId, deposit.AssetId, cancellationToken)).Name);
    }

    /// <summary>AC-4: an archived portfolio is read-only — 409 and the terms are kept.</summary>
    [Fact]
    public async Task Update_ArchivedPortfolio_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken);
        await client.ArchivePortfolioAsync(portfolioId, cancellationToken);

        var response = await client.PutAsJsonAsync(
            SavingsAccountUri(portfolioId, account.AssetId), NewUpdateSavingsAccountRequest(), cancellationToken);

        await response.AssertPortfolioArchivedConflictAsync(cancellationToken);
        var unchanged = await client.GetSavingsAccountAsync(portfolioId, account.AssetId, cancellationToken);
        Assert.Equal("Savings account", unchanged.Name);
        Assert.Equal(5.25m, unchanged.AnnualInterestRatePercent);
    }

    /// <summary>AC-12: updating an archived account is a 409 <c>Conflict.AssetArchived</c> and it keeps its terms.</summary>
    [Fact]
    public async Task Update_ArchivedAccount_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken);
        await client.ArchiveAssetAsync(portfolioId, account.AssetId, cancellationToken);

        var response = await client.PutAsJsonAsync(
            SavingsAccountUri(portfolioId, account.AssetId), NewUpdateSavingsAccountRequest(), cancellationToken);

        await response.AssertAssetArchivedConflictAsync(cancellationToken);
        var unchanged = await client.GetSavingsAccountAsync(portfolioId, account.AssetId, cancellationToken);
        Assert.Equal("Savings account", unchanged.Name);
        Assert.Equal(5.25m, unchanged.AnnualInterestRatePercent);
        Assert.False(unchanged.TaxExempt);
    }

    [Theory]
    [InlineData("name-empty")]
    [InlineData("rate-negative")]
    [InlineData("rate-over-100")]
    public async Task Update_InvalidInput_ReturnsBadRequestAndKeepsTerms(string invalidCase)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken);
        var payload = invalidCase switch
        {
            "name-empty" => NewUpdateSavingsAccountRequest(name: ""),
            "rate-negative" => NewUpdateSavingsAccountRequest(annualInterestRatePercent: -1m),
            "rate-over-100" => NewUpdateSavingsAccountRequest(annualInterestRatePercent: 100.01m),
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase), invalidCase, null)
        };

        var response = await client.PutAsJsonAsync(SavingsAccountUri(portfolioId, account.AssetId), payload, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var unchanged = await client.GetSavingsAccountAsync(portfolioId, account.AssetId, cancellationToken);
        Assert.Equal("Savings account", unchanged.Name);
        Assert.Equal(5.25m, unchanged.AnnualInterestRatePercent);
    }
}
