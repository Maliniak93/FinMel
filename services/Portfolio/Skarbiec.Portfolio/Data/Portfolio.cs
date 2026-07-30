using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class Portfolio : IUserOwned
{
    public required Guid Id { get; init; }
    public Guid UserId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string Currency { get; set; }
    public bool IsArchived { get; set; }

    /// <summary>
    /// Denormalized count of assets under this portfolio. The Asset entity doesn't exist until
    /// T1.2 (which depends on this task) — this counter lets DeletePortfolio block a hard delete
    /// on a portfolio that still has assets today; T1.2's AddAsset/RemoveAsset increment/decrement it.
    /// </summary>
    public int AssetCount { get; set; }
}
