using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Reporting.Data;

namespace Skarbiec.Reporting.Features.TargetAllocation.GetTargetAllocation;

public sealed class GetTargetAllocationHandler(ReportingDbContext db)
{
    public async Task<TargetAllocationResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var allocation = await db.TargetAllocations.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var groups = allocation?.Groups ?? [];

        // Snapshots and lines are tenancy-filtered in their own right, so this never reaches another user's rows.
        var snapshot = await db.ValuationSnapshots
            .AsNoTracking()
            .OrderByDescending(s => s.Date)
            .FirstOrDefaultAsync(cancellationToken);

        var byClass = new Dictionary<AssetClass, decimal>();
        if (snapshot is not null)
        {
            var lines = await db.AssetValuations
                .AsNoTracking()
                .Where(l => l.Date == snapshot.Date)
                .Select(l => new { l.AssetClass, l.ValuePln })
                .ToListAsync(cancellationToken);

            foreach (var line in lines)
            {
                byClass[line.AssetClass] = byClass.GetValueOrDefault(line.AssetClass) + line.ValuePln;
            }
        }

        var computation = TargetAllocationMath.Compute(groups, byClass);

        return new TargetAllocationResponse
        {
            IsConfigured = allocation is not null,
            AsOf = snapshot?.Date,
            IsStale = snapshot?.IsStale ?? false,
            TotalPln = snapshot?.TotalPln ?? 0m,
            BasePln = computation.BasePln,
            OutsidePln = computation.OutsidePln,
            OutsideAssetClasses = computation.OutsideAssetClasses,
            ByAssetClass = byClass
                .OrderByDescending(v => v.Value)
                .Select(v => new TargetAllocationAssetClassValue { AssetClass = v.Key, ValuePln = v.Value })
                .ToList(),
            Groups = groups
                .Select((group, i) =>
                {
                    var computed = computation.Groups[i];
                    return new TargetAllocationGroupResponse
                    {
                        Name = group.Name,
                        AssetClasses = group.AssetClasses,
                        TargetPercent = group.TargetPercent,
                        BandPp = group.BandPp,
                        BandLowPercent = computed.BandLowPercent,
                        BandHighPercent = computed.BandHighPercent,
                        ValuePln = computed.ValuePln,
                        CurrentPercent = computed.CurrentPercent,
                        DeviationPp = computed.DeviationPp,
                        TargetValuePln = computed.TargetValuePln,
                        DeviationPln = computed.DeviationPln,
                        IsInBand = computed.IsInBand,
                    };
                })
                .ToList(),
        };
    }
}
