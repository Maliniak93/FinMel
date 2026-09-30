using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

/// <summary>
/// One settled calendar month of a savings account's interest (savings-interest-settlement): what the
/// bank paid, fixed once stored — later edits to the history never recompute it. Periods settle in
/// order, one per month (unique <c>(AssetId, PeriodEnd)</c>), and only the latest one can be undone.
/// Written only by SettleSavingsInterest/UndoSavingsInterestSettlement; RemoveAsset and DeletePortfolio
/// delete it with its asset.
/// </summary>
public sealed class SavingsInterestSettlement : IUserOwned
{
    public required Guid Id { get; init; }
    public Guid UserId { get; set; }

    /// <summary>The Savings-class asset — a plain Guid, no FK, like every other reference in this service.</summary>
    public required Guid AssetId { get; init; }

    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required decimal GrossInterest { get; init; }
    public required decimal Tax { get; init; }

    /// <summary>
    /// The system-managed Deposit crediting the net interest, dated <see cref="PeriodEnd"/>;
    /// <see langword="null"/> when the net is 0. The transaction endpoints refuse to edit or delete it.
    /// </summary>
    public Guid? TransactionId { get; init; }
}
