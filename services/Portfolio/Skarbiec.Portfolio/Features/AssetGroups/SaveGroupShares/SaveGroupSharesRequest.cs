using System.ComponentModel.DataAnnotations;

namespace Skarbiec.Portfolio.Features.AssetGroups.SaveGroupShares;

public sealed record SaveGroupSharesRequest
{
    /// <summary>Exactly the regular groups, each once.</summary>
    [Required]
    public required IReadOnlyList<GroupShareRequest> Shares { get; init; }
}

public sealed record GroupShareRequest
{
    public required Guid GroupId { get; init; }

    /// <summary>Percent of the free funds, 0 to 100 with one decimal.</summary>
    public required decimal SharePercent { get; init; }
}
