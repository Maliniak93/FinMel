using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.CashPlaces;

internal static class CashPlaceBalances
{
    public const string BaseCurrency = "PLN";

    public static bool IsCashPlaceBalance(this Asset asset) => asset.CashPlaceId is not null;

    public static Asset NewBalance(CashPlace place, string currency) => new()
    {
        Id = Guid.NewGuid(),
        CashPlaceId = place.Id,
        GroupId = place.DefaultGroupId,
        AssetClass = AssetClass.Cash,
        Name = place.Name,
        Currency = currency,
        ValuationMode = AssetValuationMode.CurrencyValued
    };

    public static Task<List<Asset>> LoadBalancesAsync(
        this PortfolioDbContext dbContext, Guid placeId, CancellationToken cancellationToken)
        => dbContext.Assets.Where(a => a.CashPlaceId == placeId).ToListAsync(cancellationToken);

    public static async Task<CashPlaceResponse> ToResponseAsync(
        this PortfolioDbContext dbContext, CashPlace place, CancellationToken cancellationToken)
        => place.ToResponse(await dbContext.LoadBalancesAsync(place.Id, cancellationToken));
}
