namespace Skarbiec.Portfolio.Features.Planning.SavePlanningSettings;

public sealed record SavePlanningSettingsRequest
{
    /// <summary>Whole days from 0 to 90; 0 turns the payment reminders off.</summary>
    public required decimal PaymentLeadDays { get; init; }
}
