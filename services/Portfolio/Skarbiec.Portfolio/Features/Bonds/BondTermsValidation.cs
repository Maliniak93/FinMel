using System.ComponentModel.DataAnnotations;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;

namespace Skarbiec.Portfolio.Features.Bonds;

internal static class BondTermsValidation
{
    public const string SeriesCodePattern = "^[A-Z]{3}[0-9]{4}$";

    public static IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext,
        string seriesCode,
        TreasuryBondType type,
        DateOnly purchaseDate,
        decimal purchasePricePerBond,
        decimal firstPeriodRatePercent,
        decimal? marginPercent,
        decimal earlyRedemptionFeePerBond)
    {
        if (!Enum.IsDefined(type))
        {
            yield return new ValidationResult($"'{type}' is not a treasury bond type.", [nameof(TreasuryBond.Type)]);
        }
        else if (!seriesCode.StartsWith(type.ToString().ToUpperInvariant(), StringComparison.Ordinal))
        {
            yield return new ValidationResult(
                $"The series code must start with the bond type '{type.ToString().ToUpperInvariant()}'.", [nameof(TreasuryBond.SeriesCode)]);
        }

        var timeProvider = validationContext.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;
        if (purchaseDate > WarsawCalendar.Today(timeProvider))
        {
            yield return new ValidationResult("The purchase date can't be in the future.", [nameof(TreasuryBond.PurchaseDate)]);
        }

        // The columns are numeric(18,2) and numeric(7,4): a finer value is rejected rather than silently rounded.
        if (decimal.Round(purchasePricePerBond, 2) != purchasePricePerBond)
        {
            yield return new ValidationResult(
                "The price can't have more than 2 decimal places.", [nameof(TreasuryBond.PurchasePricePerBond)]);
        }

        if (decimal.Round(earlyRedemptionFeePerBond, 2) != earlyRedemptionFeePerBond)
        {
            yield return new ValidationResult(
                "The early redemption fee can't have more than 2 decimal places.", [nameof(TreasuryBond.EarlyRedemptionFeePerBond)]);
        }

        if (decimal.Round(firstPeriodRatePercent, 4) != firstPeriodRatePercent)
        {
            yield return new ValidationResult(
                "The rate can't have more than 4 decimal places.", [nameof(TreasuryBond.FirstPeriodRatePercent)]);
        }

        if (marginPercent is { } margin && decimal.Round(margin, 4) != margin)
        {
            yield return new ValidationResult(
                "The margin can't have more than 4 decimal places.", [nameof(TreasuryBond.MarginPercent)]);
        }
    }
}
