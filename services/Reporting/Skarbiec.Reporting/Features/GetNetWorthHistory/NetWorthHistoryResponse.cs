namespace Skarbiec.Reporting.Features.GetNetWorthHistory;

/// <summary>Only dates with at least one snapshot; the client interpolates across gaps.</summary>
public sealed record NetWorthHistoryResponse
{
    public required string Range { get; init; }
    public required IReadOnlyList<NetWorthHistoryPoint> Points { get; init; }

    /// <summary>Last point minus first point; null with fewer than two points.</summary>
    public decimal? ChangePln { get; init; }

    /// <summary>Change relative to the first point, in percent; null with fewer than two points or a first point of 0.</summary>
    public decimal? ChangePercent { get; init; }
}

public sealed record NetWorthHistoryPoint
{
    public required DateOnly Date { get; init; }
    public required decimal NetWorthPln { get; init; }
}
