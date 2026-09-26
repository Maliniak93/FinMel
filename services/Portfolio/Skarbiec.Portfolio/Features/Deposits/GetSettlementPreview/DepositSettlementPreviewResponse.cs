namespace Skarbiec.Portfolio.Features.Deposits.GetSettlementPreview;

/// <summary>
/// What the settle dialog is pre-filled with (term-deposits-settlement): the part-1
/// <see cref="DepositInterestMath"/> projection, settled on the maturity date.
/// </summary>
public sealed record DepositSettlementPreviewResponse
{
    public required DateOnly SettledOn { get; init; }
    public required decimal GrossInterest { get; init; }
    public required decimal Tax { get; init; }
    public required decimal NetInterest { get; init; }
    public required decimal FinalAmount { get; init; }
}
