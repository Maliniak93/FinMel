using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.Deposits.SettleDeposit;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

/// <summary>
/// term-deposits-settlement: <c>POST .../deposits/{assetId}/settle</c> with <c>{settledOn,
/// grossInterest, tax}</c> stores what the bank actually paid, credits the net interest to the
/// deposit as a system-managed Deposit transaction on <c>settledOn</c>, raises the quantity to
/// principal + net and marks the deposit Settled. The <c>AssetPositionChanged</c> written in the same
/// save is proven hostlessly by <see cref="PortfolioOutboxTests.SettleDeposit_PublishesPositionChangedWithFinalAmount"/>.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class SettleDepositEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    /// <summary>AC-3 (HTTP half).</summary>
    [Fact]
    public async Task Settle_WithPreviewValues_CreditsNetInterestAndMarksSettled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        var openingId = Assert.Single((await client.ListTransactionsAsync(portfolioId, deposit.AssetId, cancellationToken)).Items).Id;
        var previewResponse = await client.GetAsync(DepositSettlementPreviewUri(portfolioId, deposit.AssetId), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = await previewResponse.ReadJsonAsync(cancellationToken);
        var request = new SettleDepositRequest
        {
            SettledOn = DateOnly.Parse(preview.GetProperty("settledOn").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
            GrossInterest = preview.GetProperty("grossInterest").GetDecimal(),
            Tax = preview.GetProperty("tax").GetDecimal()
        };

        var response = await client.PostAsJsonAsync(SettleDepositUri(portfolioId, deposit.AssetId), request, cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Settle answered {(int)response.StatusCode}.");

        var transactions = (await client.ListTransactionsAsync(portfolioId, deposit.AssetId, cancellationToken)).Items;
        Assert.Equal(2, transactions.Count);
        var credit = Assert.Single(transactions, t => t.Id != openingId);
        Assert.Equal(TransactionType.Deposit, credit.Type);
        Assert.Equal(119.83m, credit.Quantity);
        Assert.Equal(1m, credit.UnitPrice);
        Assert.Equal(new DateOnly(2026, 4, 15), credit.Date);

        var asset = await client.GetAssetAsync(portfolioId, deposit.AssetId, cancellationToken);
        Assert.Equal(10_119.83m, asset.Quantity);
        Assert.Equal(AssetClass.Deposit, asset.AssetClass);
        Assert.True(asset.DepositSettled);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(portfolioId, deposit.AssetId, cancellationToken);

        var settled = await client.GetDepositAsync(portfolioId, deposit.AssetId, cancellationToken);
        Assert.Equal(DepositStatus.Settled, settled.Status);
        Assert.Equal(new DateOnly(2026, 4, 15), settled.SettledOn);
        Assert.Equal(147.95m, settled.SettledGrossInterest);
        Assert.Equal(28.12m, settled.SettledTax);
    }

    /// <summary>AC-4: the bank paid something other than the projection — the overrides are what is stored and credited.</summary>
    [Fact]
    public async Task Settle_WithOverriddenAmounts_UsesThem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        var request = NewSettleRequest(settledOn: new DateOnly(2026, 4, 17), grossInterest: 150.00m, tax: 28.50m);

        var response = await client.PostAsJsonAsync(SettleDepositUri(portfolioId, deposit.AssetId), request, cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Settle answered {(int)response.StatusCode}.");

        var settled = await client.GetDepositAsync(portfolioId, deposit.AssetId, cancellationToken);
        Assert.Equal(DepositStatus.Settled, settled.Status);
        Assert.Equal(new DateOnly(2026, 4, 17), settled.SettledOn);
        Assert.Equal(150.00m, settled.SettledGrossInterest);
        Assert.Equal(28.50m, settled.SettledTax);

        var asset = await client.GetAssetAsync(portfolioId, deposit.AssetId, cancellationToken);
        Assert.Equal(10_121.50m, asset.Quantity);

        var credit = Assert.Single(
            (await client.ListTransactionsAsync(portfolioId, deposit.AssetId, cancellationToken)).Items,
            t => t.Date == new DateOnly(2026, 4, 17));
        Assert.Equal(TransactionType.Deposit, credit.Type);
        Assert.Equal(121.50m, credit.Quantity);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(portfolioId, deposit.AssetId, cancellationToken);
    }

    /// <summary>
    /// A settlement with no net interest (gross = tax, here both 0) adds no transaction: the quantity
    /// stays at the principal, yet the deposit is Settled.
    /// </summary>
    [Fact]
    public async Task Settle_ZeroNetInterest_AddsNoTransactionAndMarksSettled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);

        var response = await client.PostAsJsonAsync(
            SettleDepositUri(portfolioId, deposit.AssetId), NewSettleRequest(grossInterest: 0m, tax: 0m), cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Settle answered {(int)response.StatusCode}.");
        var asset = await client.GetAssetAsync(portfolioId, deposit.AssetId, cancellationToken);
        Assert.Equal(10_000m, asset.Quantity);
        Assert.Equal(1, asset.TransactionCount);
        Assert.True(asset.DepositSettled);
        var settled = await client.GetDepositAsync(portfolioId, deposit.AssetId, cancellationToken);
        Assert.Equal(DepositStatus.Settled, settled.Status);
        Assert.Equal(0m, settled.SettledGrossInterest);
        Assert.Equal(0m, settled.SettledTax);
    }

    /// <summary>
    /// ADR-026: the net-interest credit freezes the PLN rate of its own date (<c>settledOn</c>), while
    /// the opening transaction keeps the start date's rate.
    /// </summary>
    [Fact]
    public async Task Settle_ForeignCurrencyDeposit_FreezesFxRateOfSettlementDate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        Factory.FxRateLookupClient
            .WithRate("EUR", new DateOnly(2026, 1, 15), 4.30m)
            .WithRate("EUR", new DateOnly(2026, 4, 17), 4.25m);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken, NewDepositRequest(currency: "EUR"));

        var response = await client.PostAsJsonAsync(
            SettleDepositUri(portfolioId, deposit.AssetId), NewSettleRequest(settledOn: new DateOnly(2026, 4, 17)), cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Settle answered {(int)response.StatusCode}.");
        await using var dbContext = CreateDbContext(userId);
        var transactions = await dbContext.Transactions
            .Where(t => t.AssetId == deposit.AssetId)
            .OrderBy(t => t.Date)
            .ToListAsync(cancellationToken);
        Assert.Equal(2, transactions.Count);
        Assert.Equal(4.30m, transactions[0].FxRateToPln);
        Assert.Equal(new DateOnly(2026, 4, 17), transactions[1].Date);
        Assert.Equal(119.83m, transactions[1].Quantity);
        Assert.Equal(4.25m, transactions[1].FxRateToPln);
    }

    /// <summary>AC-2 (settle half): the maturity date is tomorrow in Warsaw — 409, and nothing changes.</summary>
    [Fact]
    public async Task Settle_BeforeMaturity_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Warsaw 2026-04-14 12:00 — the day before the 2026-04-15 maturity.
        Factory.Clock.SetUtcNow(new DateTimeOffset(2026, 4, 14, 10, 0, 0, TimeSpan.Zero));
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        // settledOn is today and after the start: valid on its own, so only the maturity rule can reject it.
        var request = NewSettleRequest(settledOn: new DateOnly(2026, 4, 14));

        var response = await client.PostAsJsonAsync(SettleDepositUri(portfolioId, deposit.AssetId), request, cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.DepositNotDueErrorCode, cancellationToken);
        await client.AssertDepositUnsettledAsync(portfolioId, deposit.AssetId, cancellationToken);
    }

    /// <summary>AC-5: every rejected input is a 400 and leaves the deposit exactly as it was.</summary>
    [Theory]
    [InlineData("tax-over-gross")]
    [InlineData("gross-negative")]
    [InlineData("tax-negative")]
    [InlineData("settled-on-in-future")]
    [InlineData("settled-on-before-start")]
    public async Task Settle_InvalidInput_ReturnsBadRequest(string invalidCase)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Today is Warsaw 2026-04-20; the deposit started 2026-01-15.
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        var request = invalidCase switch
        {
            "tax-over-gross" => NewSettleRequest(grossInterest: 147.95m, tax: 147.96m),
            "gross-negative" => NewSettleRequest(grossInterest: -1m, tax: 0m),
            "tax-negative" => NewSettleRequest(tax: -0.01m),
            "settled-on-in-future" => NewSettleRequest(settledOn: new DateOnly(2026, 4, 21)),
            "settled-on-before-start" => NewSettleRequest(settledOn: new DateOnly(2026, 1, 14)),
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase), invalidCase, null)
        };

        var response = await client.PostAsJsonAsync(SettleDepositUri(portfolioId, deposit.AssetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await client.AssertDepositUnsettledAsync(portfolioId, deposit.AssetId, cancellationToken);
    }

    /// <summary>AC-7 (settle half): a second settlement is a 409 and the first one stands.</summary>
    [Fact]
    public async Task Settle_Twice_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        await client.SettleDepositAsync(portfolioId, deposit.AssetId, cancellationToken);

        var response = await client.PostAsJsonAsync(
            SettleDepositUri(portfolioId, deposit.AssetId),
            NewSettleRequest(settledOn: new DateOnly(2026, 4, 18), grossInterest: 200m, tax: 38m),
            cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.DepositAlreadySettledErrorCode, cancellationToken);
        var asset = await client.GetAssetAsync(portfolioId, deposit.AssetId, cancellationToken);
        Assert.Equal(10_119.83m, asset.Quantity);
        Assert.Equal(2, asset.TransactionCount);
        var settled = await client.GetDepositAsync(portfolioId, deposit.AssetId, cancellationToken);
        Assert.Equal(new DateOnly(2026, 4, 15), settled.SettledOn);
        Assert.Equal(147.95m, settled.SettledGrossInterest);
        Assert.Equal(28.12m, settled.SettledTax);
    }

    /// <summary>A deposit of an archived portfolio is read-only: settling it is a 409 and nothing changes.</summary>
    [Fact]
    public async Task Settle_ArchivedPortfolio_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        await client.ArchivePortfolioAsync(portfolioId, cancellationToken);

        var response = await client.PostAsJsonAsync(SettleDepositUri(portfolioId, deposit.AssetId), NewSettleRequest(), cancellationToken);

        await response.AssertPortfolioArchivedConflictAsync(cancellationToken);
        await client.AssertDepositUnsettledAsync(portfolioId, deposit.AssetId, cancellationToken);
    }

    /// <summary>The deposit endpoints address Deposit-class assets only — a Cash asset is not a deposit.</summary>
    [Fact]
    public async Task Settle_NonDepositAsset_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, _) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        var cashId = await client.AddCashAssetAsync(portfolioId, cancellationToken);

        var response = await client.PostAsJsonAsync(SettleDepositUri(portfolioId, cashId), NewSettleRequest(), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var cash = await client.GetAssetAsync(portfolioId, cashId, cancellationToken);
        Assert.Equal(0m, cash.Quantity);
        Assert.Equal(0, cash.TransactionCount);
        Assert.Null(cash.DepositSettled);
    }
}
