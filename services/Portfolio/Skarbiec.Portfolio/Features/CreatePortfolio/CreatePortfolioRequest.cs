using System.ComponentModel.DataAnnotations;
using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.CreatePortfolio;

public sealed record CreatePortfolioRequest
{
    [Required, MaxLength(200)]
    public required string Name { get; init; }

    [MaxLength(1000)]
    public string? Description { get; init; }

    [SupportedCurrency]
    public string Currency { get; init; } = Money.BaseCurrency;
}
