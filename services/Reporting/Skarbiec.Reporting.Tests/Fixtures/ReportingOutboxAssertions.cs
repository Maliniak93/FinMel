using System.Text.Json;
using MassTransit.EntityFrameworkCoreIntegration;
using MassTransit.Serialization;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Reporting.Data;

namespace Skarbiec.Reporting.Tests.Fixtures;

internal static class ReportingOutboxAssertions
{
    // MassTransit's own serializer options: its converters write a decimal as a JSON string, which plain options cannot read.
    private static readonly JsonSerializerOptions Options = SystemTextJsonMessageSerializer.Options;

    public static async Task<IReadOnlyList<T>> ReadPublishedAsync<T>(
        this ReportingDbContext dbContext, CancellationToken cancellationToken)
    {
        var rows = await dbContext.Set<OutboxMessage>()
            .Where(m => m.MessageType.Contains(typeof(T).Name))
            .OrderBy(m => m.SequenceNumber)
            .ToListAsync(cancellationToken);

        return rows.Select(row =>
        {
            using var document = JsonDocument.Parse(row.Body);
            return document.RootElement.GetProperty("message").Deserialize<T>(Options)!;
        }).ToList();
    }
}
