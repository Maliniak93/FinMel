using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Bonds.AddBond;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class AddBondValidatorTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Theory]
    [InlineData("series-type-mismatch", nameof(AddBondRequest.SeriesCode))]
    [InlineData("series-lowercase", nameof(AddBondRequest.SeriesCode))]
    [InlineData("count-zero", nameof(AddBondRequest.BondCount))]
    [InlineData("price-over-100", nameof(AddBondRequest.PurchasePricePerBond))]
    [InlineData("rate-negative", nameof(AddBondRequest.FirstPeriodRatePercent))]
    [InlineData("fee-negative", nameof(AddBondRequest.EarlyRedemptionFeePerBond))]
    [InlineData("purchase-date-tomorrow", nameof(AddBondRequest.PurchaseDate))]
    public async Task Add_InvalidInput_ReturnsBadRequestWithFieldError(string invalidCase, string field)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var valid = NewBondRequest();
        var request = invalidCase switch
        {
            "series-type-mismatch" => valid with { Type = TreasuryBondType.Coi },
            "series-lowercase" => valid with { SeriesCode = "edo1036" },
            "count-zero" => valid with { BondCount = 0 },
            "price-over-100" => valid with { PurchasePricePerBond = 100.01m },
            "rate-negative" => valid with { FirstPeriodRatePercent = -0.01m },
            "fee-negative" => valid with { EarlyRedemptionFeePerBond = -0.01m },
            "purchase-date-tomorrow" => valid with { PurchaseDate = new DateOnly(2026, 10, 2) },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase), invalidCase, null)
        };

        var response = await client.PostAsJsonAsync(BondsUri(portfolioId), request, cancellationToken);

        await response.AssertFieldValidationErrorAsync(field, cancellationToken);
        await using var dbContext = CreateDbContext(userId);
        Assert.Equal(0, await dbContext.Assets.CountAsync(cancellationToken));
        Assert.Equal(0, await dbContext.Transactions.CountAsync(cancellationToken));
        Assert.Equal(0, await dbContext.Set<TreasuryBond>().CountAsync(cancellationToken));
    }
}
