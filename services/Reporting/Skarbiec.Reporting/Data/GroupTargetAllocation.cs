using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Reporting.Data;

// One per user and asset group; GroupId is Portfolio's id, no FK, so saving replaces the whole allocation.
public sealed class GroupTargetAllocation : IUserOwned
{
    public Guid UserId { get; set; }

    public required Guid GroupId { get; set; }

    public required DateTimeOffset UpdatedAt { get; set; }

    // In the order the user saved them.
    public List<TargetAllocationGroup> Groups { get; set; } = [];
}
