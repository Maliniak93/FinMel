using System.ComponentModel.DataAnnotations;
using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.Metals.UpdateMetal;

public sealed record UpdateMetalRequest : IValidatableObject
{
    [Required, MaxLength(200)]
    public required string Name { get; init; }

    public required Metal Metal { get; init; }

    /// <summary>Fine weight of one piece, in WeightUnit.</summary>
    [Range(typeof(decimal), "0", "79228162514264337593543950335", MinimumIsExclusive = true)]
    public required decimal FineWeight { get; init; }

    public required WeightUnit WeightUnit { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        MetalWeight.Validate(Metal, WeightUnit);
}
