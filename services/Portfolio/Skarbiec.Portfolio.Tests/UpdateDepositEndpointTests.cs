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

/// <summary>
/// term-deposits: <c>PUT .../deposits/{assetId}</c> rewrites the terms, the system-managed opening
/// transaction and the asset's quantity together — never a correction transaction. Currency is not
/// part of the request (immutable).
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class UpdateDepositEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    /// <summary>AC-8 (HTTP half; the single event is proven by <see cref="PortfolioOutboxTests.UpdateDeposit_PublishesOnePositionChangedWithNewQuantity"/>).</summary>
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

    /// <summary>AC-7: updating a deposit of an archived portfolio is a 409 and the deposit keeps its terms.</summary>
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

    /// <summary>
    /// term-deposits-settlement AC-7 (update half): a settled deposit's terms are immutable — 409
    /// <c>Conflict.DepositSettled</c>, and the terms, settlement and quantity stay as they were.
    /// </summary>
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

    /// <summary>The same validation as AddDeposit applies; a rejected update leaves every row as it was.</summary>
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

    /// <summary>The deposit endpoint addresses Deposit-class assets only — any other asset is not a deposit.</summary>
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
