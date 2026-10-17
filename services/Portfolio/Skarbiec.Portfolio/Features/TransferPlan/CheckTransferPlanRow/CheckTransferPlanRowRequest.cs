namespace Skarbiec.Portfolio.Features.TransferPlan.CheckTransferPlanRow;

public sealed record CheckTransferPlanRowRequest
{
    /// <summary>True when the transfer was made, false to clear the mark.</summary>
    public required bool Checked { get; init; }
}
