using System.ComponentModel.DataAnnotations;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Deposits;

/// <summary>
/// The cross-field rules AddDepositRequest and UpdateDepositRequest share (term-deposits), keyed on
/// the request field names so the deposit form maps each message back onto its control. The simple
/// ranges live on the records as DataAnnotations.
/// </summary>
internal static class DepositTermsValidation
{
    public const int MaxTermDays = 3650;
    public const int MaxTermMonths = 120;

    public static IEnumerable<ValidationResult> Validate(
        decimal principal,
        int termLength,
        DepositTermUnit termUnit,
        decimal annualInterestRatePercent,
        DepositCapitalization capitalization,
        decimal earlyBreakInterestLossPercent)
    {
        // The stored columns are numeric(18,2), numeric(7,4) and numeric(5,2): a finer value would be
        // rounded silently by the database, so it is rejected instead.
        if (decimal.Round(principal, 2) != principal)
        {
            yield return new ValidationResult(
                "The principal can't have more than 2 decimal places.", [nameof(TermDeposit.Principal)]);
        }

        if (decimal.Round(annualInterestRatePercent, 4) != annualInterestRatePercent)
        {
            yield return new ValidationResult(
                "The interest rate can't have more than 4 decimal places.", [nameof(TermDeposit.AnnualInterestRatePercent)]);
        }

        if (decimal.Round(earlyBreakInterestLossPercent, 2) != earlyBreakInterestLossPercent)
        {
            yield return new ValidationResult(
                "The early-break interest loss can't have more than 2 decimal places.",
                [nameof(TermDeposit.EarlyBreakInterestLossPercent)]);
        }

        if (!Enum.IsDefined(termUnit))
        {
            yield return new ValidationResult($"Unknown term unit '{termUnit}'.", [nameof(TermDeposit.TermUnit)]);
        }
        else if (termUnit == DepositTermUnit.Months && termLength > MaxTermMonths)
        {
            yield return new ValidationResult(
                $"A term is at most {MaxTermMonths} months.", [nameof(TermDeposit.TermLength)]);
        }

        if (!Enum.IsDefined(capitalization))
        {
            yield return new ValidationResult(
                $"Unknown capitalization '{capitalization}'.", [nameof(TermDeposit.Capitalization)]);
        }
    }
}
