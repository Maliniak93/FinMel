using System.Net;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

/// <summary>
/// savings-interest-settlement: <c>GET .../savings-accounts/{assetId}/interest-preview</c> projects the
/// next due calendar month from the account's daily balance, or answers 409 when none is due.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class SavingsInterestPreviewEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    /// <summary>AC-4.</summary>
    [Fact]
    public async Task Preview_EndedPeriod_ReturnsProjection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken, NewInterestAccountRequest());

        var response = await client.GetAsync(SavingsInterestPreviewUri(portfolioId, account.AssetId), cancellationToken);

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

    /// <summary>AC-4: September is still running on its last day.</summary>
    [Fact]
    public async Task Preview_NothingDue_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberLastDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken, NewInterestAccountRequest());

        var response = await client.GetAsync(SavingsInterestPreviewUri(portfolioId, account.AssetId), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.SavingsInterestNotDueErrorCode, cancellationToken);
    }

    /// <summary>The preview offers the oldest unsettled month and counts every ended one.</summary>
    [Fact]
    public async Task Preview_SeveralMonthsEnded_OffersTheOldestAndCountsAll()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(NovemberEndedUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken, NewInterestAccountRequest());

        var preview = await client.GetSavingsInterestPreviewAsync(portfolioId, account.AssetId, cancellationToken);

        Assert.Equal("2026-09-30", preview.GetProperty("periodEnd").GetString());
        Assert.Equal(3, preview.GetProperty("duePeriodCount").GetInt32());
    }

    /// <summary>A tax-exempt account previews a tax of 0 and a net equal to the gross.</summary>
    [Fact]
    public async Task Preview_TaxExemptAccount_HasNoTax()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken, NewInterestAccountRequest(taxExempt: true));

        var preview = await client.GetSavingsInterestPreviewAsync(portfolioId, account.AssetId, cancellationToken);

        Assert.Equal(41.10m, preview.GetProperty("grossInterest").GetDecimal());
        Assert.Equal(0m, preview.GetProperty("tax").GetDecimal());
        Assert.Equal(41.10m, preview.GetProperty("netInterest").GetDecimal());
    }

    /// <summary>A month with no interest (a 0 % account) is never due.</summary>
    [Fact]
    public async Task Preview_ZeroRateAccount_IsNeverDue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(NovemberEndedUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(
            cancellationToken, NewInterestAccountRequest(annualInterestRatePercent: 0m));

        var response = await client.GetAsync(SavingsInterestPreviewUri(portfolioId, account.AssetId), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.SavingsInterestNotDueErrorCode, cancellationToken);
    }

    /// <summary>The savings-account endpoints address savings accounts only: another asset class is a 404.</summary>
    [Fact]
    public async Task Preview_ForCashAsset_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var cashId = await client.AddCashAssetAsync(portfolioId, cancellationToken);

        var response = await client.GetAsync(SavingsInterestPreviewUri(portfolioId, cashId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// savings-cash-transfers AC-5: a 5 000 opening deposit on 1 September plus a Cash → Savings transfer
    /// of 5 000 on 16 September averages 7 500 over September (5 000 for 15 days, 10 000 for 15).
    /// </summary>
    [Fact]
    public async Task Preview_CountsTransferFromItsDate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var setup = await client.CreateCashAndSavingsAsync(
            cancellationToken,
            cashBalance: 10_000m,
            toppedUpOn: InterestOpeningDate,
            savingsRequest: NewSavingsAccountRequest(annualInterestRatePercent: 5m, openingAmount: 5_000m, openingDate: InterestOpeningDate));
        await client.CreateTransferAsync(
            cancellationToken, NewTransferRequest(setup.CashAssetId, setup.SavingsAssetId, 5_000m, new DateOnly(2026, 9, 16)));

        var preview = await client.GetSavingsInterestPreviewAsync(setup.SavingsPortfolioId, setup.SavingsAssetId, cancellationToken);

        Assert.Equal(7_500.00m, preview.GetProperty("averageDailyBalance").GetDecimal());
        Assert.Equal(30.82m, preview.GetProperty("grossInterest").GetDecimal());
    }

    [Fact]
    public async Task Preview_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync(SavingsInterestPreviewUri(Guid.NewGuid(), Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
