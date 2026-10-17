using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Messaging;

// Ordered by SavedAtUtc: an older message than the stored one is dropped, an equal one is the same state.
public sealed class TargetAllocationSavedConsumer(PortfolioDbContext db, ILogger<TargetAllocationSavedConsumer> logger)
    : IConsumer<TargetAllocationSaved>
{
    public async Task Consume(ConsumeContext<TargetAllocationSaved> context)
    {
        var message = context.Message;
        var cancellationToken = context.CancellationToken;

        // IgnoreQueryFilters: a consumer has no request user and writes for the user the event names.
        var replica = await db.TargetAllocationReplicas
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(r => r.UserId == message.UserId, cancellationToken);

        List<TargetAllocationReplicaGroup> groups =
        [
            .. message.Groups.Select(g => new TargetAllocationReplicaGroup(g.Name, [.. g.AssetClasses], g.TargetPercent, g.BandPp)),
        ];

        if (replica is null)
        {
            db.TargetAllocationReplicas.Add(new TargetAllocationReplica
            {
                UserId = message.UserId,
                Groups = groups,
                SavedAtUtc = message.SavedAtUtc,
            });
        }
        else if (message.SavedAtUtc < replica.SavedAtUtc)
        {
            logger.LogInformation(
                "TargetAllocationSavedConsumer: dropping out-of-order event for user {UserId} ({EventSavedAt} < stored {StoredSavedAt}).",
                message.UserId, message.SavedAtUtc, replica.SavedAtUtc);
            return;
        }
        else
        {
            replica.Groups = groups;
            replica.SavedAtUtc = message.SavedAtUtc;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
