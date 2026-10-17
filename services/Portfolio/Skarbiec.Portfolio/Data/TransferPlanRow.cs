using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.TransferPlan;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class TransferPlanRow : IUserOwned
{
    public required Guid Id { get; init; }
    public Guid UserId { get; set; }
    public required Guid BatchId { get; init; }
    public required Guid BucketId { get; init; }
    public required string BucketName { get; init; }
    public required Guid? AccountId { get; init; }
    public required string? AccountName { get; init; }
    public required string? GroupName { get; init; }

    /// <summary>The group's classes that can be bought on the account; empty for a row without a group.</summary>
    public required List<AssetClass> AssetClasses { get; init; }

    public required decimal AmountPln { get; init; }
    public required TransferMarker Marker { get; init; }
    public required int Position { get; init; }

    public DateTimeOffset? CheckedAtUtc { get; set; }
}
