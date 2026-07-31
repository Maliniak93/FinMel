using Skarbiec.Contracts;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class Transaction : IUserOwned
{
    public required Guid Id { get; init; }
    public Guid UserId { get; set; }
    public required Guid AssetId { get; init; }
    public required TransactionType Type { get; set; }
    public decimal Quantity { get; set; }

    /// <summary>
    /// Reuses the owning <see cref="Asset.Currency"/> rather than storing a redundant currency
    /// code here — mirrors the <see cref="Asset.ManualValueAmount"/> decision (T1.2).
    /// </summary>
    public decimal UnitPriceAmount { get; set; }

    public decimal FeeAmount { get; set; }
    public required DateOnly Date { get; set; }
}
