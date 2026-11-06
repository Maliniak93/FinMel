using MassTransit;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;

namespace Skarbiec.Reporting.Tests.Fixtures;

// Arrange only: facts assert on what Reporting made of the event, never on the publish.
internal static class PositionEvents
{
    public static async Task<Guid> PublishCashPositionAsync(
        this IBus bus,
        Guid userId,
        Guid? accountId,
        decimal amount,
        CancellationToken cancellationToken,
        Guid? assetId = null,
        long version = 0,
        bool isArchived = false,
        IReadOnlyList<QuantityPoint>? quantityHistory = null,
        AssetClass assetClass = AssetClass.Cash,
        IReadOnlyList<ClassPoint>? classHistory = null,
        Guid? groupId = null,
        bool outsideNetWorth = false)
    {
        var id = assetId ?? Guid.NewGuid();
        await bus.Publish(new AssetPositionChanged
        {
            GroupId = groupId,
            AssetId = id,
            AccountId = accountId,
            UserId = userId,
            AssetClass = assetClass,
            ValuationMode = AssetValuationMode.CurrencyValued,
            Currency = "PLN",
            Quantity = amount,
            QuoteUnitsPerQuantity = 1m,
            ClassHistory = classHistory ?? [],
            QuantityHistory = quantityHistory ?? [],
            IsArchived = isArchived,
            OutsideNetWorth = outsideNetWorth,
            Version = version,
            OccurredAtUtc = DateTimeOffset.UtcNow,
        }, cancellationToken);

        return id;
    }

    public static AssetPositionChanged BondPositionEvent(
        Guid userId,
        Guid? accountId,
        DateOnly purchaseDate,
        Guid? assetId = null,
        long version = 0,
        int bondCount = 50,
        decimal pricePerBond = 100m,
        decimal firstPeriodRatePercent = 6.5m,
        TreasuryBondType type = TreasuryBondType.Edo,
        string seriesCode = "EDO0235",
        IReadOnlyDictionary<int, decimal>? settledPeriodRates = null) => new()
        {
            AssetId = assetId ?? Guid.NewGuid(),
            AccountId = accountId,
            UserId = userId,
            AssetClass = AssetClass.Bond,
            ValuationMode = AssetValuationMode.CurrencyValued,
            Currency = "PLN",
            Quantity = bondCount * pricePerBond,
            QuoteUnitsPerQuantity = 1m,
            QuantityHistory = [new QuantityPoint { Date = purchaseDate, Quantity = bondCount * pricePerBond }],
            ClassHistory = [],
            IsArchived = false,
            OutsideNetWorth = false,
            BondTerms = new()
            {
                SeriesCode = seriesCode,
                Type = type,
                PurchaseDate = purchaseDate,
                PurchasePricePerBond = pricePerBond,
                FirstPeriodRatePercent = firstPeriodRatePercent,
                MarginPercent = 2m,
                BondCountHistory = new Dictionary<DateOnly, int> { [purchaseDate] = bondCount },
                SettledPeriodRates = settledPeriodRates ?? new Dictionary<int, decimal>(),
                IsRedeemed = false,
            },
            Version = version,
            OccurredAtUtc = DateTimeOffset.UtcNow,
        };

    public static PositionBondTerms NewBondTerms(
        DateOnly purchaseDate,
        IReadOnlyDictionary<DateOnly, int> bondCountHistory,
        TreasuryBondType type = TreasuryBondType.Edo,
        string seriesCode = "EDO0235",
        decimal firstPeriodRatePercent = 6.5m,
        IReadOnlyDictionary<int, decimal>? settledPeriodRates = null) => new()
        {
            SeriesCode = seriesCode,
            Type = type,
            PurchaseDate = purchaseDate,
            PurchasePricePerBond = 100m,
            FirstPeriodRatePercent = firstPeriodRatePercent,
            MarginPercent = 2m,
            BondCountHistory = bondCountHistory,
            SettledPeriodRates = settledPeriodRates ?? new Dictionary<int, decimal>(),
            IsRedeemed = false,
        };

    public static async Task SeedBondPositionAsync(
        this ReportingDbContext db, AssetPositionChanged evt, CancellationToken cancellationToken)
    {
        db.Positions.Add(new Position
        {
            AssetId = evt.AssetId,
            UserId = evt.UserId,
            AccountId = evt.AccountId,
            AssetClass = evt.AssetClass,
            ValuationMode = evt.ValuationMode,
            Currency = evt.Currency,
            Quantity = evt.Quantity,
            IsArchived = false,
            Version = evt.Version,
            UpdatedAt = DateTimeOffset.UtcNow,
            QuantityHistory = [.. evt.QuantityHistory.Select(p => new PositionQuantityPoint { Date = p.Date, Quantity = p.Quantity })],
            BondTerms = evt.BondTerms,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public static AssetRemoved AssetRemovedEvent(Guid assetId, Guid? accountId, Guid userId) => new()
    {
        AssetId = assetId,
        AccountId = accountId,
        UserId = userId,
        OccurredAtUtc = DateTimeOffset.UtcNow,
    };
}
