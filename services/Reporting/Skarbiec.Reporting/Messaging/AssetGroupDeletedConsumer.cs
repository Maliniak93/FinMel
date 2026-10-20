using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;

namespace Skarbiec.Reporting.Messaging;

public sealed class AssetGroupDeletedConsumer(ReportingDbContext db) : IConsumer<AssetGroupDeleted>
{
    public async Task Consume(ConsumeContext<AssetGroupDeleted> context)
    {
        var message = context.Message;

        // IgnoreQueryFilters: a consumer has no request user and writes for the user the event names.
        await db.GroupTargetAllocations
            .IgnoreQueryFilters()
            .Where(a => a.UserId == message.UserId && a.GroupId == message.GroupId)
            .ExecuteDeleteAsync(context.CancellationToken);
    }
}
