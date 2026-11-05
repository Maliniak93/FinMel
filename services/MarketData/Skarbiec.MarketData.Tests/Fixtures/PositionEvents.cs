using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;

namespace Skarbiec.MarketData.Tests.Fixtures;

public static class PositionEvents
{
    // A currency-valued cash position: it carries no instrument, so only the FX need can react to it.
    public static AssetPositionChanged CashChanged(Guid assetId, long version, DateOnly? firstTransactionDate) => new()
    {
        AssetId = assetId,
        AccountId = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        AssetClass = AssetClass.Cash,
        ValuationMode = AssetValuationMode.CurrencyValued,
        InstrumentId = null,
        Currency = "USD",
        Quantity = 1m,
        QuoteUnitsPerQuantity = 1m,
        ClassHistory = [],
        QuantityHistory = [],
        IsArchived = false,
        OutsideNetWorth = false,
        Version = version,
        FirstTransactionDate = firstTransactionDate,
        OccurredAtUtc = DateTimeOffset.UtcNow,
    };
}
