namespace Skarbiec.Portfolio.Features.Deposits.GetSettlementPreview;

public sealed record DepositSettlementPreviewResponse
{
    public required DateOnly SettledOn { get; init; }
    public required decimal GrossInterest { get; init; }
    public required decimal Tax { get; init; }
    public required decimal NetInterest { get; init; }
    public required decimal FinalAmount { get; init; }
}
