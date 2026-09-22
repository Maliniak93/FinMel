namespace Skarbiec.Portfolio.Features;

public sealed record PortfolioResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Currency { get; init; }
    public required bool IsArchived { get; init; }

    /// <summary>How many assets the portfolio holds — counted from the Assets table, never a stored counter (spec-02).</summary>
    public required int AssetCount { get; init; }
}

public static class PortfolioMappingExtensions
{
    /// <summary>
    /// spec-02: <paramref name="assetCount"/> is a parameter because the count lives in the Assets
    /// table, not on the row — read slices project it as a correlated subquery inside their own
    /// query, and a slice that already knows the number (a freshly created portfolio holds none)
    /// passes it directly.
    /// </summary>
    public static PortfolioResponse ToResponse(this PortfolioEntity portfolio, int assetCount) => new()
    {
        Id = portfolio.Id,
        Name = portfolio.Name,
        Description = portfolio.Description,
        Currency = portfolio.Currency,
        IsArchived = portfolio.IsArchived,
        AssetCount = assetCount
    };
}
