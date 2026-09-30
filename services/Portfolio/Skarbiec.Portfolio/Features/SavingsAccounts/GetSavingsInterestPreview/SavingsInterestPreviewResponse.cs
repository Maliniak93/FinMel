namespace Skarbiec.Portfolio.Features.SavingsAccounts.GetSavingsInterestPreview;

/// <summary>
/// What the settle-interest dialog is pre-filled with (savings-interest-settlement): the
/// <see cref="SavingsInterestMath"/> projection of the next due calendar month.
/// </summary>
public sealed record SavingsInterestPreviewResponse
{
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }

    /// <summary>The account's current rate, applied to every day of the period.</summary>
    public required decimal AnnualInterestRatePercent { get; init; }

    public required decimal AverageDailyBalance { get; init; }
    public required decimal GrossInterest { get; init; }
    public required decimal Tax { get; init; }
    public required decimal NetInterest { get; init; }

    /// <summary>Ended, unsettled months with interest — this one included.</summary>
    public required int DuePeriodCount { get; init; }
}
