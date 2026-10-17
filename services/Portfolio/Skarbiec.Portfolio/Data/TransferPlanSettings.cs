using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class TransferPlanSettings : IUserOwned
{
    public const int DefaultMinimumTransferPln = 100;
    public const int MaxMinimumTransferPln = 1_000_000;

    public Guid UserId { get; set; }

    /// <summary>Whole złoty; 0 means no minimum.</summary>
    public required int MinimumTransferPln { get; set; }
}
