using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Planning.Recurrence;

namespace Skarbiec.Portfolio.Features.AllocationPlan;

internal static class AllocationPlanVersions
{
    public static AllocationPlanVersion? InForce(this IEnumerable<AllocationPlanVersion> versions, YearMonth month) =>
        versions
            .Where(v => v.EffectiveFrom <= month.FirstDay)
            .OrderByDescending(v => v.EffectiveFrom)
            .FirstOrDefault();
}
