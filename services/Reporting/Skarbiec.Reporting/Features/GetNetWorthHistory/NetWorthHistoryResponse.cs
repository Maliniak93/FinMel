namespace Skarbiec.Reporting.Features.GetNetWorthHistory;

/// <summary>Only dates with at least one snapshot; the client interpolates across gaps.</summary>
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
