using System.Text.Json;
using Skarbiec.Reporting.Valuation;

namespace Skarbiec.Reporting.Data;

/// <summary>
/// (De)serializes <see cref="ValuationSnapshot.BreakdownJson"/>. Stored as plain
/// <c>System.Text.Json</c>-serialized text mapped to a <c>jsonb</c> column (<see cref="ReportingDbContext"/>)
/// rather than through EF's own JSON-owned-entity mapping — the project's first JSONB column, and
/// this keeps the read/write path independent of whichever native JSON-column support the installed
/// Npgsql provider version happens to have, while the column still stores real <c>jsonb</c> (queryable
/// with Postgres's own jsonb operators later if ever needed, e.g. for T2.12's dashboard breakdown).
/// </summary>
public static class ValuationBreakdown
{
    public static string Serialize(IReadOnlyList<AssetClassBreakdownEntry> entries) => JsonSerializer.Serialize(entries);

    public static IReadOnlyList<AssetClassBreakdownEntry> Deserialize(string json) =>
        JsonSerializer.Deserialize<IReadOnlyList<AssetClassBreakdownEntry>>(json) ?? [];
}
