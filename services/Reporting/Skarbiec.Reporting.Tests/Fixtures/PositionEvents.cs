using MassTransit;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;

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
        IReadOnlyList<QuantityPoint>? quantityHistory = null)
    {
        var id = assetId ?? Guid.NewGuid();
        await bus.Publish(new AssetPositionChanged
        {
            AssetId = id,
            AccountId = accountId,
            UserId = userId,
            AssetClass = AssetClass.Cash,
            ValuationMode = AssetValuationMode.CurrencyValued,
            Currency = "PLN",
            Quantity = amount,
            QuoteUnitsPerQuantity = 1m,
            ClassHistory = [],
            QuantityHistory = quantityHistory ?? [],
            IsArchived = isArchived,
            Version = version,
            OccurredAtUtc = DateTimeOffset.UtcNow,
        }, cancellationToken);

        return id;
    }

    public static AssetRemoved AssetRemovedEvent(Guid assetId, Guid? accountId, Guid userId) => new()
    {
        AssetId = assetId,
        AccountId = accountId,
        UserId = userId,
        OccurredAtUtc = DateTimeOffset.UtcNow,
    };
}
