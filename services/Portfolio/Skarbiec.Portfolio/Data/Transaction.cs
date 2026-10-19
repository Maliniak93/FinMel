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

    public decimal UnitPriceAmount { get; set; }

    public decimal? FxRateToPln { get; set; }

    public decimal? AmountPln { get; set; }

    public decimal? FeeAmount { get; set; }

    public required DateOnly Date { get; set; }

    public Guid? TransferId { get; set; }

    // Sequence value taken at insert and never changed by an edit; orders transactions entered on one date.
    public long EntryOrder { get; private set; }
}
