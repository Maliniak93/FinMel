namespace Skarbiec.Portfolio.Features.Planning.Split;

public sealed record GroupShareInput(Guid GroupId, string Name, decimal SharePercent);

public sealed record GroupAllocation(Guid GroupId, decimal AllocationPln);

public sealed record GroupSplit(
    decimal CushionPln,
    IReadOnlyList<GroupAllocation> Groups,
    decimal UnassignedPln,
    decimal DeficitPln);

public static class GroupSplitCalculator
{
    // The cushion takes at most what it misses, in whole zloty; the rest goes to the Regular groups by share, each floored to whole zloty,
    // and the remainder to the largest share (ties by name), so the amounts always sum to the free funds.
    public static GroupSplit Split(decimal freeFundsPln, decimal cushionMissingPln, IReadOnlyList<GroupShareInput> shares)
    {
        if (freeFundsPln <= 0m)
        {
            return new GroupSplit(0m, [.. shares.Select(s => new GroupAllocation(s.GroupId, 0m))], 0m, -freeFundsPln);
        }

        var cushion = Math.Floor(Math.Min(freeFundsPln, Math.Max(0m, cushionMissingPln)));
        var rest = freeFundsPln - cushion;

        if (shares.Count == 0)
        {
            return new GroupSplit(cushion, [], rest, 0m);
        }

        var floored = shares.ToDictionary(s => s.GroupId, s => Math.Floor(rest * s.SharePercent / 100m));
        var largest = shares
            .OrderByDescending(s => s.SharePercent)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .First();
        floored[largest.GroupId] += rest - floored.Values.Sum();

        return new GroupSplit(cushion, [.. shares.Select(s => new GroupAllocation(s.GroupId, floored[s.GroupId]))], 0m, 0m);
    }
}
