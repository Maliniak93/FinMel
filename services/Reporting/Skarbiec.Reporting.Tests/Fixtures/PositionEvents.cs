using MassTransit;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;

namespace Skarbiec.Reporting.Tests.Fixtures;

/// <summary>
/// The <c>AssetPositionChanged</c> payloads Portfolio would publish, as arrange-step publishes onto a
/// bus — the host's own (<c>Factory.Services</c>) or a bare consumer provider's. Arrange only: the
/// facts assert on what Reporting made of the event, never on the publish itself.
/// </summary>
internal static class PositionEvents
{
    /// <summary>
    /// A PLN cash position worth <paramref name="amount"/> — what <c>AddAsset</c> publishes for new
    /// cash (<paramref name="version"/> 0), and, with the same <paramref name="assetId"/> and a
    /// higher <paramref name="version"/>, what the archive/restore fan-out publishes for it
    /// (<paramref name="portfolioIsArchived"/> flipped). Returns the asset id it published for.
    /// </summary>
    public static async Task<Guid> PublishCashPositionAsync(
        this IBus bus,
        Guid userId,
        Guid portfolioId,
        decimal amount,
        CancellationToken cancellationToken,
        Guid? assetId = null,
        bool portfolioIsArchived = false,
        long version = 0)
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
            PortfolioIsArchived = portfolioIsArchived,
            Version = version,
            OccurredAtUtc = DateTimeOffset.UtcNow,
        }, cancellationToken);

        return id;
    }
}
