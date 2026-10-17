namespace Skarbiec.Portfolio.Features.TransferPlan.SaveTransferPlanSettings;

public sealed record SaveTransferPlanSettingsRequest
{
    /// <summary>Whole PLN from 0 to 1 000 000; 0 means no minimum.</summary>
    public required decimal MinimumTransferPln { get; init; }
}
