namespace Skarbiec.Portfolio.Features.SavingsAccounts.GetSavingsInterestPreview;

public sealed record SavingsInterestPreviewResponse
{
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }

    public required decimal AnnualInterestRatePercent { get; init; }

    public required decimal AverageDailyBalance { get; init; }
    public required decimal GrossInterest { get; init; }
    public required decimal Tax { get; init; }
    public required decimal NetInterest { get; init; }

    /// <summary>Ended, unsettled months with interest, this one included.</summary>
    public required int DuePeriodCount { get; init; }
}
