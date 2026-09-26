using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

/// <summary>
/// The terms of a Deposit-class <see cref="Asset"/> (term-deposits), 1:1 with it: <see cref="AssetId"/>
/// is both the primary key and the FK to the asset row in this same database. Written only by the
/// AddDeposit/UpdateDeposit/SettleDeposit slices; the principal reaches <see cref="Asset.Quantity"/>
/// solely through the asset's system-managed opening Deposit transaction, and the settled net
/// interest through a second system-managed Deposit transaction (ADR-009).
/// </summary>
public sealed class TermDeposit : IUserOwned
{
    public required Guid AssetId { get; init; }
    public Guid UserId { get; set; }
    public string? BankName { get; set; }
    public required decimal Principal { get; set; }
    public required DateOnly StartDate { get; set; }
    public required int TermLength { get; set; }
    public required DepositTermUnit TermUnit { get; set; }

    /// <summary>Derived from <see cref="StartDate"/>, <see cref="TermLength"/> and <see cref="TermUnit"/> on every write; stored so reads can filter and sort on it.</summary>
    public required DateOnly MaturityDate { get; set; }

    public required decimal AnnualInterestRatePercent { get; set; }
    public required DepositCapitalization Capitalization { get; set; }

    /// <summary>IKE/IKZE — no Belka tax on the interest.</summary>
    public bool TaxExempt { get; set; }

    /// <summary>The share of the accrued interest lost on an early break. Informational only.</summary>
    public decimal EarlyBreakInterestLossPercent { get; set; } = 100m;

    /// <summary>
    /// The settlement date (term-deposits-settlement): set by SettleDeposit together with
    /// <see cref="SettledGrossInterest"/> and <see cref="SettledTax"/>, <see langword="null"/> until
    /// then. A settled deposit's terms are immutable.
    /// </summary>
    public DateOnly? SettledOn { get; set; }

    /// <summary>The gross interest the bank actually paid — may differ from the projection.</summary>
    public decimal? SettledGrossInterest { get; set; }

    /// <summary>The Belka tax the bank actually withheld.</summary>
    public decimal? SettledTax { get; set; }
}
