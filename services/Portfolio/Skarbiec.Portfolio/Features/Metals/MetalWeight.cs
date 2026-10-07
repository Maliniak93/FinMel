using System.ComponentModel.DataAnnotations;
using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.Metals;

internal static class MetalWeight
{
    public const decimal GramsPerTroyOunce = 31.1034768m;

    // FineWeightGramsPerPiece is numeric(18,8).
    public static decimal ToGrams(decimal fineWeight, WeightUnit unit) =>
        Math.Round(unit == WeightUnit.TroyOunce ? fineWeight * GramsPerTroyOunce : fineWeight, 8, MidpointRounding.AwayFromZero);

    public static decimal TotalFineGrams(decimal pieces, decimal fineWeightGramsPerPiece) =>
        Math.Round(pieces * fineWeightGramsPerPiece, 8, MidpointRounding.AwayFromZero);

    public static IEnumerable<ValidationResult> Validate(Metal metal, WeightUnit weightUnit)
    {
        if (!Enum.IsDefined(metal))
        {
            yield return new ValidationResult($"'{metal}' is not a precious metal.", ["Metal"]);
        }

        if (!Enum.IsDefined(weightUnit))
        {
            yield return new ValidationResult($"'{weightUnit}' is not a weight unit.", ["WeightUnit"]);
        }
    }
}
