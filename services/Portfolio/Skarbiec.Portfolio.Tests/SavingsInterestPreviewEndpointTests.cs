using System.Net;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class SavingsInterestPreviewEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
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

    [Fact]
    public async Task Preview_CountsDepositPayoutFromItsDate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (depositPortfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        await client.SettleDepositAsync(
            depositPortfolioId, deposit.AssetId, cancellationToken, NewSettleRequest(grossInterest: 0m, tax: 0m));
        var (savingsPortfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(
            cancellationToken, NewSavingsAccountRequest(annualInterestRatePercent: 5m, withOpeningDeposit: false), portfolioName: "Wallet");
        await client.PayOutDepositAsync(
            depositPortfolioId, deposit.AssetId, account.AssetId, cancellationToken, new DateOnly(2026, 9, 16));

        var preview = await client.GetSavingsInterestPreviewAsync(savingsPortfolioId, account.AssetId, cancellationToken);

        Assert.Equal("2026-09-16", preview.GetProperty("periodStart").GetString());
        Assert.Equal("2026-09-30", preview.GetProperty("periodEnd").GetString());
        Assert.Equal(10_000.00m, preview.GetProperty("averageDailyBalance").GetDecimal());
        Assert.Equal(20.55m, preview.GetProperty("grossInterest").GetDecimal());
    }
}
