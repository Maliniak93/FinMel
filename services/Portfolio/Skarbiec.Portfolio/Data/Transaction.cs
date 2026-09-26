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

    /// <summary>
    /// The <c>{Asset.Currency}PLN</c> rate frozen at write time — the latest MarketData rate on or
    /// before <see cref="Date"/> (ADR-026); <c>1</c> for a PLN asset. <see langword="null"/> when
    /// MarketData had no rate that early, so the transaction's PLN value is unknown.
    /// </summary>
    public decimal? FxRateToPln { get; set; }

    public required DateOnly Date { get; set; }

    /// <summary>
    /// The link of a transfer leg (asset-transfers-deposit-funding): a transfer is exactly two
    /// transactions sharing this id — a Withdraw on the source asset and a Deposit on the target, same
    /// quantity, same date, unit price 1. Written only by a transfer's entry point (e.g. AddDeposit),
    /// never by the transaction endpoints; <see langword="null"/> on an ordinary transaction and on a
    /// leg whose counterpart's asset was removed (detached).
    /// </summary>
    public Guid? TransferId { get; set; }
}
