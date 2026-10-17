using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Messaging;

// Ordered by (Date, OccurredAtUtc): a rebuild of past days that republishes the same latest date never rolls the replica back.
public sealed class LatestValuationChangedConsumer(PortfolioDbContext db, ILogger<LatestValuationChangedConsumer> logger)
    : IConsumer<LatestValuationChanged>
{
    public async Task Consume(ConsumeContext<LatestValuationChanged> context)
    {
        var message = context.Message;
        var cancellationToken = context.CancellationToken;

        // IgnoreQueryFilters: a consumer has no request user and writes for the user the event names.
        var replica = await db.ValuationReplicas
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(r => r.UserId == message.UserId, cancellationToken);

        List<ValuationReplicaClassValue> byAssetClass =
            [.. message.ByAssetClass.Select(v => new ValuationReplicaClassValue(v.AssetClass, v.ValuePln))];

        if (replica is null)
        {
            db.ValuationReplicas.Add(new ValuationReplica
            {
                UserId = message.UserId,
                Date = message.Date,
                IsStale = message.IsStale,
                TotalPln = message.TotalPln,
                ByAssetClass = byAssetClass,
                OccurredAtUtc = message.OccurredAtUtc,
            });
        }
        else if (message.Date < replica.Date || (message.Date == replica.Date && message.OccurredAtUtc < replica.OccurredAtUtc))
        {
            logger.LogInformation(
                "LatestValuationChangedConsumer: dropping out-of-order event for user {UserId} ({EventDate} {EventOccurredAt} before stored {StoredDate} {StoredOccurredAt}).",
                message.UserId, message.Date, message.OccurredAtUtc, replica.Date, replica.OccurredAtUtc);
            return;
        }
        else
        {
            replica.Date = message.Date;
            replica.IsStale = message.IsStale;
            replica.TotalPln = message.TotalPln;
            replica.ByAssetClass = byAssetClass;
            replica.OccurredAtUtc = message.OccurredAtUtc;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
