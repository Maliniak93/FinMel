namespace Skarbiec.Reporting.Features.GetNetWorthHistory;

/// <summary>
/// Daily net-worth series for the chart (E5, T2.13). Only dates with at least one snapshot are
/// included — the client interpolates visually across gaps (weekends/holidays the sync job skipped,
/// or a stretch before the account existed) instead of the server padding every calendar day.
/// </summary>
public sealed record NetWorthHistoryResponse
{
    public required string Range { get; init; }
    public required IReadOnlyList<NetWorthHistoryPoint> Points { get; init; }
}

public sealed record NetWorthHistoryPoint
{
    public required DateOnly Date { get; init; }
    public required decimal NetWorthPln { get; init; }
}
