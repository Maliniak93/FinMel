using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Reporting.Data;

namespace Skarbiec.Reporting.Features.GetNetWorthHistory;

public sealed class GetNetWorthHistoryHandler(ReportingDbContext db, TimeProvider timeProvider)
{
    private const string CustomRange = "CUSTOM";

    public async Task<Result<NetWorthHistoryResponse>> HandleAsync(
        string? range,
        AssetClass? assetClass,
        CancellationToken cancellationToken,
        Guid? groupId = null,
        bool ungrouped = false,
        DateOnly? from = null,
        DateOnly? to = null)
    {
        if (groupId is not null && ungrouped)
        {
            return NetWorthHistoryErrors.InvalidGroupFilter;
        }

        if (assetClass is { } requestedClass && !Enum.IsDefined(requestedClass))
        {
            return NetWorthHistoryErrors.InvalidAssetClass(requestedClass);
        }

        var windowResult = ResolveWindow(range, from, to);
        if (windowResult.IsFailure)
        {
            return windowResult.Error;
        }

        var (rangeName, start, end) = windowResult.Value;

        var firstDate = await db.ValuationSnapshots
            .AsNoTracking()
            .Select(s => (DateOnly?)s.Date)
            .MinAsync(cancellationToken);
        var lastDate = await db.ValuationSnapshots
            .AsNoTracking()
            .Select(s => (DateOnly?)s.Date)
            .MaxAsync(cancellationToken);

        DateOnly? baselineDate = null;
        if (start is { } windowStart)
        {
            baselineDate = await db.ValuationSnapshots
                .AsNoTracking()
                .Where(s => s.Date < windowStart)
                .Select(s => (DateOnly?)s.Date)
                .MaxAsync(cancellationToken);
        }

        var lower = baselineDate ?? start;

        var points = await db.ValuationSnapshots
            .AsNoTracking()
            .Where(s => lower == null || s.Date >= lower)
            .Where(s => end == null || s.Date <= end)
            .OrderBy(s => s.Date)
            .Select(s => new { s.Date, NetWorthPln = s.TotalPln, IsIncomplete = false })
            .ToListAsync(cancellationToken);

        if (assetClass is null && groupId is null && !ungrouped)
        {
            var flaggedDates = (await db.AssetValuations
                    .AsNoTracking()
                    .Where(v => v.IsStale && v.ValuePln == 0m && v.Quantity > 0m)
                    .Where(v => lower == null || v.Date >= lower)
                    .Where(v => end == null || v.Date <= end)
                    .Select(v => v.Date)
                    .Distinct()
                    .ToListAsync(cancellationToken))
                .ToHashSet();

            points = points
                .Select(p => new { p.Date, p.NetWorthPln, IsIncomplete = flaggedDates.Contains(p.Date) })
                .ToList();
        }
        else if (assetClass is { } filterClass && groupId is null && !ungrouped)
        {
            var classTotals = await db.AssetValuations
                .AsNoTracking()
                .Where(v => v.AssetClass == filterClass)
                .Where(v => lower == null || v.Date >= lower)
                .Where(v => end == null || v.Date <= end)
                .GroupBy(v => v.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    ValuePln = g.Sum(v => v.ValuePln),
                    IsIncomplete = g.Any(v => v.IsStale && v.ValuePln == 0m && v.Quantity > 0m),
                })
                .ToDictionaryAsync(t => t.Date, cancellationToken);

            points = points
                .Select(p => new
                {
                    p.Date,
                    NetWorthPln = classTotals.TryGetValue(p.Date, out var total) ? total.ValuePln : 0m,
                    IsIncomplete = classTotals.TryGetValue(p.Date, out var flagged) && flagged.IsIncomplete,
                })
                .ToList();
        }
        else if (groupId is not null || ungrouped)
        {
            // The group is the position's current one, read at query time; a line with no position has no match.
            var lines = db.AssetValuations
                .AsNoTracking()
                .Where(v => lower == null || v.Date >= lower)
                .Where(v => end == null || v.Date <= end)
                .Join(db.Positions, v => v.AssetId, p => p.AssetId, (v, p) => new
                {
                    v.Date,
                    v.AssetClass,
                    v.ValuePln,
                    IsMissing = v.IsStale && v.ValuePln == 0m && v.Quantity > 0m,
                    p.GroupId,
                })
                .Where(x => x.GroupId == groupId);

            if (assetClass is { } groupClass)
            {
                lines = lines.Where(x => x.AssetClass == groupClass);
            }

            var groupTotals = await lines
                .GroupBy(x => x.Date)
                .Select(g => new { Date = g.Key, ValuePln = g.Sum(x => x.ValuePln), IsIncomplete = g.Any(x => x.IsMissing) })
                .ToDictionaryAsync(t => t.Date, cancellationToken);

            points = points
                .Select(p => new
                {
                    p.Date,
                    NetWorthPln = groupTotals.TryGetValue(p.Date, out var total) ? total.ValuePln : 0m,
                    IsIncomplete = groupTotals.TryGetValue(p.Date, out var flagged) && flagged.IsIncomplete,
                })
                .ToList();
        }

        var baseline = baselineDate is null ? null : points.FirstOrDefault(p => p.Date == baselineDate);
        var windowPoints = baseline is null ? points : points.Where(p => p.Date != baselineDate).ToList();

        decimal? changePln = null;
        decimal? changePercent = null;
        decimal? baselineValue = null;
        if (windowPoints.Count > 0)
        {
            baselineValue = baseline is not null
                ? baseline.NetWorthPln
                : windowPoints.Count >= 2 ? windowPoints[0].NetWorthPln : null;
        }

        if (baselineValue is { } baselineAmount)
        {
            changePln = windowPoints[^1].NetWorthPln - baselineAmount;
            changePercent = baselineAmount == 0m ? null : Math.Round(changePln.Value / baselineAmount * 100m, 2);
        }

        return new NetWorthHistoryResponse
        {
            Range = rangeName,
            Points = windowPoints
                .Select(p => new NetWorthHistoryPoint { Date = p.Date, NetWorthPln = p.NetWorthPln, IsIncomplete = p.IsIncomplete })
                .ToList(),
            ChangePln = changePln,
            ChangePercent = changePercent,
            FirstDate = firstDate,
            LastDate = lastDate,
        };
    }

    // Resolved against today, not the latest snapshot, so a stale sync never shrinks the window; MAX has no lower bound.
    private Result<(string Range, DateOnly? Start, DateOnly? End)> ResolveWindow(string? range, DateOnly? from, DateOnly? to)
    {
        if (from is not null || to is not null)
        {
            if (range is not null)
            {
                return NetWorthHistoryErrors.RangeWithDates;
            }

            if (from is not { } customFrom || to is not { } customTo)
            {
                return NetWorthHistoryErrors.IncompleteDates;
            }

            if (customFrom > customTo)
            {
                return NetWorthHistoryErrors.FromAfterTo;
            }

            var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
            return (CustomRange, customFrom, customTo < today ? customTo : today);
        }

        var presetToday = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var preset = range ?? "1Y";
        DateOnly? start;

        switch (preset.ToUpperInvariant())
        {
            case "1M":
                start = presetToday.AddMonths(-1);
                break;
            case "1Y":
                start = presetToday.AddYears(-1);
                break;
            case "YTD":
                start = new DateOnly(presetToday.Year, 1, 1);
                break;
            case "MAX":
                start = null;
                break;
            default:
                return NetWorthHistoryErrors.InvalidRange(preset);
        }

        return (preset.ToUpperInvariant(), start, null);
    }
}
