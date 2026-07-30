namespace Skarbiec.Portfolio.Features;

public sealed record PortfolioResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Currency { get; init; }
    public required bool IsArchived { get; init; }
}

public static class PortfolioMappingExtensions
{
    public static PortfolioResponse ToResponse(this PortfolioEntity portfolio) => new()
    {
        Id = portfolio.Id,
        Name = portfolio.Name,
        Description = portfolio.Description,
        Currency = portfolio.Currency,
        IsArchived = portfolio.IsArchived
    };
}
