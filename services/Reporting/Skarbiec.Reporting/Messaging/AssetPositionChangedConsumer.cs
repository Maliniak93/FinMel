using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;

namespace Skarbiec.Reporting.Messaging;

/// <summary>
/// Upserts Reporting's local <see cref="Position"/> read model from <see cref="AssetPositionChanged"/>
/// (spec-03, ADR-021). The event carries the full position state, so nothing here calls Portfolio
/// back — that is the whole point of the read model.
/// </summary>
/// <remarks>
/// Ordering is by the event's own per-asset <c>Version</c> counter, not by a timestamp: clocks
/// across a publisher and a consumer are not a total order, the counter is (spec-03 design
/// decision 4). An event older than the stored row is dropped so an out-of-order redelivery cannot
/// resurrect an older quantity; an equal version is the same state, so applying it is harmless.
/// </remarks>
public sealed class AssetPositionChangedConsumer(
    ReportingDbContext db,
    ILogger<AssetPositionChangedConsumer> logger) : IConsumer<AssetPositionChanged>
{
    public async Task Consume(ConsumeContext<AssetPositionChanged> context)
    {
        var message = context.Message;
        var cancellationToken = context.CancellationToken;

        // Bypasses the tenancy filter deliberately (IgnoreQueryFilters): a consumer has no request
        // user — ICurrentUser.UserId is Guid.Empty — and writes on behalf of whichever user the
        // event names. UserId below is set from the event, which is why UserOwnedSaveInterceptor's
        // "already set" escape hatch exists.
        var position = await db.Positions
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(p => p.AssetId == message.AssetId, cancellationToken);

        if (position is null)
        {
            db.Positions.Add(new Position
            {
                AssetId = message.AssetId,
                UserId = message.UserId,
                PortfolioId = message.PortfolioId,
                AssetClass = message.AssetClass,
                ValuationMode = message.ValuationMode,
                InstrumentId = message.InstrumentId,
                Currency = message.Currency,
                Quantity = message.Quantity,
                ManualValueAmount = message.ManualValueAmount,
                ManualValueDate = message.ManualValueDate,
                PortfolioIsArchived = message.PortfolioIsArchived,
                Version = message.Version,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        else if (message.Version < position.Version)
        {
            logger.LogInformation(
                "AssetPositionChangedConsumer: dropping out-of-order event for asset {AssetId} (version {EventVersion} < stored {StoredVersion}).",
                message.AssetId, message.Version, position.Version);
            return;
        }
        else
        {
            position.UserId = message.UserId;
            position.PortfolioId = message.PortfolioId;
            position.AssetClass = message.AssetClass;
            position.ValuationMode = message.ValuationMode;
            position.InstrumentId = message.InstrumentId;
            position.Currency = message.Currency;
            position.Quantity = message.Quantity;
            position.ManualValueAmount = message.ManualValueAmount;
            position.ManualValueDate = message.ManualValueDate;
            position.PortfolioIsArchived = message.PortfolioIsArchived;
            position.Version = message.Version;
            position.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
