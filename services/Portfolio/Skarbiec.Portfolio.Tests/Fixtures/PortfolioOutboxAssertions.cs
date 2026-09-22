using System.Text.Json;
using MassTransit.EntityFrameworkCoreIntegration;
using MassTransit.Serialization;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Tests.Fixtures;

/// <summary>
/// Reads back the outbox rows written by <see cref="PortfolioDbContext"/>'s EF outbox, deserializing
/// the MassTransit envelope's <c>message</c> property into the event type a spec-02 outbox fact wants
/// to assert on — the plain <c>MessageType.Contains(...)</c> check other outbox tests use only proves
/// *something* was published, not what it carried.
/// </summary>
internal static class PortfolioOutboxAssertions
{
    // MassTransit's own options, not hand-rolled ones: it writes the envelope with converters of its
    // own (a decimal lands as a JSON string, for one), so a plain case-insensitive JsonSerializerOptions
    // throws on the very fields these facts assert.
    private static readonly JsonSerializerOptions Options = SystemTextJsonMessageSerializer.Options;

    /// <summary>All outbox rows whose <c>MessageType</c> names <typeparamref name="T"/>, deserialized from the envelope's <c>message</c> payload, in write order.</summary>
    public static async Task<IReadOnlyList<T>> ReadPublishedAsync<T>(
        this PortfolioDbContext dbContext, CancellationToken cancellationToken)
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
