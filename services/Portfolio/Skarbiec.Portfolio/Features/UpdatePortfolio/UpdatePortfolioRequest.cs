using System.ComponentModel.DataAnnotations;
using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.UpdatePortfolio;

public sealed record UpdatePortfolioRequest
{
    [Required, MaxLength(200)]
    public required string Name { get; init; }

    [MaxLength(1000)]
    public string? Description { get; init; }

    [Required, SupportedCurrency]
    public required string Currency { get; init; }
}
