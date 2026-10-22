using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class PlanningSettings : IUserOwned
{
    public const int DefaultPaymentLeadDays = 7;
    public const int MaxPaymentLeadDays = 90;

    public Guid UserId { get; set; }

    /// <summary>Days ahead an upcoming payment is reminded; 0 turns the reminder off.</summary>
    public required int PaymentLeadDays { get; set; }
}
