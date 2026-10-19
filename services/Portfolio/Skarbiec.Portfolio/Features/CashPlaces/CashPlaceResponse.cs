using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.CashPlaces;

public sealed record CashBalanceResponse
{
    public required string Currency { get; init; }
    public required Guid CashAssetId { get; init; }
    public required decimal Balance { get; init; }

    /// <summary>Balance at the latest rate, rounded to 2 places; null when the rate is unavailable, and not computed by the writes.</summary>
    public decimal? ValuePln { get; init; }
}

public sealed record CashPlaceResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required bool IsArchived { get; init; }

    /// <summary>The group every new balance of the place takes.</summary>
    public Guid? GroupId { get; init; }

    public required IReadOnlyList<CashBalanceResponse> Balances { get; init; }

    /// <summary>Null when any balance has no rate, and not computed by the writes.</summary>
    public decimal? ValuePln { get; init; }
}

public sealed record CashTotalResponse
{
    public required string Currency { get; init; }
    public required decimal Balance { get; init; }
}

public sealed record CashPlacesResponse
{
    public required IReadOnlyList<CashPlaceResponse> Places { get; init; }
    public required IReadOnlyList<CashTotalResponse> Totals { get; init; }

    /// <summary>Null when any balance has no rate.</summary>
    public decimal? TotalValuePln { get; init; }

    /// <summary>True when a held foreign balance could not be valued because MarketData gave no rate.</summary>
    public required bool FxUnavailable { get; init; }
}

public static class CashPlaceMappingExtensions
{
    public static CashPlaceResponse ToResponse(this CashPlace place, IEnumerable<Asset> balances) => new()
    {
        Id = place.Id,
        Name = place.Name,
        IsArchived = place.IsArchived,
        GroupId = place.DefaultGroupId,
        Balances = [.. balances
            .OrderBy(b => b.Currency == CashPlaceBalances.BaseCurrency ? 0 : 1)
            .ThenBy(b => b.Currency, StringComparer.Ordinal)
            .Select(b => new CashBalanceResponse { Currency = b.Currency, CashAssetId = b.Id, Balance = b.Quantity })]
    };
}
