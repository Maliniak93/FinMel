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
        Guid portfolioId,
        decimal amount,
        CancellationToken cancellationToken,
        Guid? assetId = null,
        bool portfolioIsArchived = false,
        long version = 0,
        IReadOnlyList<QuantityPoint>? quantityHistory = null)
    {
        var id = assetId ?? Guid.NewGuid();
        await bus.Publish(new AssetPositionChanged
        {
            AssetId = id,
            PortfolioId = portfolioId,
            UserId = userId,
            AssetClass = AssetClass.Cash,
            ValuationMode = AssetValuationMode.CurrencyValued,
            Currency = "PLN",
            Quantity = amount,
            QuoteUnitsPerQuantity = 1m,
            QuantityHistory = quantityHistory ?? [],
            PortfolioIsArchived = portfolioIsArchived,
            IsArchived = false,
            Version = version,
            OccurredAtUtc = DateTimeOffset.UtcNow,
        }, cancellationToken);

        return id;
    }
}
