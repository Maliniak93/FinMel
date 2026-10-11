using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Valuation;

namespace Skarbiec.Reporting.Messaging;

// Ordered by the per-asset Version, not a timestamp: an older event is dropped, an equal one is the same state.
public sealed class AssetPositionChangedConsumer(
    ReportingDbContext db,
    SnapshotWriter snapshotWriter,
    ILogger<AssetPositionChangedConsumer> logger) : IConsumer<AssetPositionChanged>
{
    public async Task Consume(ConsumeContext<AssetPositionChanged> context)
    {
        var message = context.Message;
        var cancellationToken = context.CancellationToken;
        var occurredOn = HistoryChange.UtcDate(message.OccurredAtUtc);

        DateOnly? earliestAffected;

        // IgnoreQueryFilters: a consumer has no request user and writes for the user the event names.
        var position = await db.Positions
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(p => p.AssetId == message.AssetId, cancellationToken);

        if (position is null)
        {
            earliestAffected = HistoryChange.EarliestAffectedDate(null, message);
            db.Positions.Add(new Position
            {
                AssetId = message.AssetId,
                UserId = message.UserId,
                AccountId = message.AccountId,
                AssetClass = message.AssetClass,
                ValuationMode = message.ValuationMode,
                InstrumentId = message.InstrumentId,
                Currency = message.Currency,
                Quantity = message.Quantity,
                QuoteUnitsPerQuantity = message.QuoteUnitsPerQuantity,
                IsArchived = message.IsArchived,
                Version = message.Version,
                UpdatedAt = DateTimeOffset.UtcNow,
                QuantityHistory = ToHistory(message),
                ArchivedOn = message.IsArchived ? occurredOn : null,
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
            earliestAffected = HistoryChange.EarliestAffectedDate(position, message);
            position.UserId = message.UserId;
            position.AccountId = message.AccountId;
            position.AssetClass = message.AssetClass;
            position.ValuationMode = message.ValuationMode;
            position.InstrumentId = message.InstrumentId;
            position.Currency = message.Currency;
            position.Quantity = message.Quantity;
            position.QuoteUnitsPerQuantity = message.QuoteUnitsPerQuantity;
            position.IsArchived = message.IsArchived;
            position.Version = message.Version;
            position.UpdatedAt = DateTimeOffset.UtcNow;
            position.QuantityHistory = ToHistory(message);

            position.ArchivedOn = message.IsArchived ? position.ArchivedOn ?? occurredOn : null;
        }

        if (earliestAffected is { } from && from < snapshotWriter.Today)
        {
            await HistoryRebuildRequests.RequestAsync(db, context, message.UserId, from, cancellationToken);
        }

        // Saved first so the revaluation reads this position, inside the same inbox transaction.
        await db.SaveChangesAsync(cancellationToken);

        await snapshotWriter.RevalueTodayAsync(message.UserId, cancellationToken);
    }

    private static List<PositionQuantityPoint> ToHistory(AssetPositionChanged message) =>
        [.. message.QuantityHistory.Select(p => new PositionQuantityPoint { Date = p.Date, Quantity = p.Quantity })];
}
