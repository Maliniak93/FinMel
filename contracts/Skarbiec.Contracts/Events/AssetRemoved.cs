namespace Skarbiec.Contracts.Events;

/// <summary>Terminal for the AssetId, so it carries no version; the inbox covers redelivery.</summary>
public sealed record AssetRemoved
{
    public required Guid AssetId { get; init; }
    public required Guid PortfolioId { get; init; }
    public required Guid UserId { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }

    /// <summary>Part of a portfolio delete: a PortfolioDeleted for the same portfolio is published in the same transaction.</summary>
    public required bool CascadedFromPortfolio { get; init; }
}
