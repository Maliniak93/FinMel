namespace Skarbiec.Portfolio.Features.Planning.GetPlanningSettings;

public sealed record PlanningSettingsResponse
{
    /// <summary>Whole PLN; 0 means no minimum.</summary>
    public required int MinimumTransferPln { get; init; }

    /// <summary>Days ahead an upcoming payment is reminded; 0 turns the reminder off.</summary>
    public required int PaymentLeadDays { get; init; }
}
