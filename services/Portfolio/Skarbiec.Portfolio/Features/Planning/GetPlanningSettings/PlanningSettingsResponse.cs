namespace Skarbiec.Portfolio.Features.Planning.GetPlanningSettings;

public sealed record PlanningSettingsResponse
{
    /// <summary>Days ahead an upcoming payment is reminded; 0 turns the reminder off.</summary>
    public required int PaymentLeadDays { get; init; }
}
