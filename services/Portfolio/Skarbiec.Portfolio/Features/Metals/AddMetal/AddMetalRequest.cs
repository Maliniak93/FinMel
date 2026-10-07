using System.ComponentModel.DataAnnotations;
using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.Metals.AddMetal;

public sealed record AddMetalRequest : IValidatableObject
{
    [Required, MaxLength(200)]
    public required string Name { get; init; }

    public required Metal Metal { get; init; }

    /// <summary>Fine weight of one piece, in WeightUnit.</summary>
    [Range(typeof(decimal), "0", "79228162514264337593543950335", MinimumIsExclusive = true)]
    public required decimal FineWeight { get; init; }

    public required WeightUnit WeightUnit { get; init; }

    /// <summary>Omitted: the holding starts with no pieces.</summary>
    public FirstPurchaseRequest? FirstPurchase { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        MetalWeight.Validate(Metal, WeightUnit);
}

/// <summary>Its date must not be after today (Europe/Warsaw), which the handler checks.</summary>
public sealed record FirstPurchaseRequest
{
    [Range(typeof(decimal), "0", "79228162514264337593543950335", MinimumIsExclusive = true)]
    public required decimal Pieces { get; init; }

    /// <summary>PLN per piece.</summary>
    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public required decimal PricePerPiece { get; init; }

    public required DateOnly Date { get; init; }
}
