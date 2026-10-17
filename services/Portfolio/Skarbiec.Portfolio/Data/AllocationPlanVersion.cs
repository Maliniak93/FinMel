using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class AllocationPlanVersion : IUserOwned
{
    public required Guid Id { get; init; }
    public Guid UserId { get; set; }
    public required DateOnly EffectiveFrom { get; init; }

    public Guid? ReserveAccountId { get; set; }

    public List<AllocationPlanEntry> Entries { get; init; } = [];
}
