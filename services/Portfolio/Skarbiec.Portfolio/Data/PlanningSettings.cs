using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class PlanningSettings : IUserOwned
{
    public const int DefaultMinimumTransferPln = 100;
    public const int MaxMinimumTransferPln = 1_000_000;
    public const int DefaultPaymentLeadDays = 7;
    public const int MaxPaymentLeadDays = 90;

    public Guid UserId { get; set; }

    /// <summary>Whole złoty; 0 means no minimum.</summary>
    public required int MinimumTransferPln { get; set; }

    /// <summary>Days ahead an upcoming payment is reminded; 0 turns the reminder off.</summary>
    public required int PaymentLeadDays { get; set; }
}
