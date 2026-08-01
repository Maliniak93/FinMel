namespace Skarbiec.Contracts.Events;

/// <summary>Published by Portfolio when a transaction is recorded against an asset.</summary>
public sealed record TransactionRecorded
{
    public required Guid TransactionId { get; init; }
    public required Guid AssetId { get; init; }
    public required Guid UserId { get; init; }
    public required TransactionType Type { get; init; }
    public required decimal Quantity { get; init; }
    public required DateOnly Date { get; init; }
}
