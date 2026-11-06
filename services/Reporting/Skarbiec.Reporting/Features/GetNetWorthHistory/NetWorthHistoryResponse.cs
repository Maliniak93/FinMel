namespace Skarbiec.Reporting.Features.GetNetWorthHistory;

/// <summary>Only dates with at least one snapshot; the client interpolates across gaps.</summary>
public sealed record NetWorthHistoryResponse
{
    public required string Range { get; init; }
    public required IReadOnlyList<NetWorthHistoryPoint> Points { get; init; }

    /// <summary>Last point minus the last point before the window (the first point when none is earlier); null without a baseline.</summary>
    public decimal? ChangePln { get; init; }

    /// <summary>Change relative to that baseline, in percent; null without a baseline or with a baseline of 0.</summary>
    public decimal? ChangePercent { get; init; }

    /// <summary>The user's earliest snapshot date, independent of range and filter; null without snapshots.</summary>
    public DateOnly? FirstDate { get; init; }

    /// <summary>The user's latest snapshot date, independent of range and filter; null without snapshots.</summary>
    public DateOnly? LastDate { get; init; }
}

public sealed record NetWorthHistoryPoint
{
    public required DateOnly Date { get; init; }
    public required decimal NetWorthPln { get; init; }

    /// <summary>True when a line in scope has no price or rate for the day, so the total leaves it out.</summary>
    public required bool IsIncomplete { get; init; }
}
