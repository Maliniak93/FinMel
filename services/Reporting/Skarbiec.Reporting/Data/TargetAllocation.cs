using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Reporting.Data;

// One per user: the UserId is the key, so saving replaces the whole allocation.
public sealed class TargetAllocation : IUserOwned
{
    public Guid UserId { get; set; }

    public required DateTimeOffset UpdatedAt { get; set; }

    // In the order the user saved them.
    public List<TargetAllocationGroup> Groups { get; set; } = [];
}
