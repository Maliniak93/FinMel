namespace Skarbiec.Portfolio.Features.Planning.SavePlanningSettings;

public sealed record SavePlanningSettingsRequest
{
    /// <summary>Whole PLN from 0 to 1 000 000; 0 means no minimum.</summary>
    public required decimal MinimumTransferPln { get; init; }

    /// <summary>Whole days from 0 to 90; 0 turns the payment reminders off.</summary>
    public required decimal PaymentLeadDays { get; init; }
}
