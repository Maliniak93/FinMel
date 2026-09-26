namespace Skarbiec.Portfolio.Data;

/// <summary>
/// How often a term deposit's interest is capitalised (term-deposits): each capitalisation period is
/// taxed on its own and its net interest compounds into the next one.
/// </summary>
public enum DepositCapitalization
{
    /// <summary>One period, from the start date to maturity.</summary>
    AtMaturity,

    /// <summary>Periods end at <c>StartDate.AddMonths(k)</c>, the last one cut at maturity.</summary>
    Monthly,

    /// <summary>Periods end at <c>StartDate.AddMonths(3k)</c>, the last one cut at maturity.</summary>
    Quarterly,

    /// <summary>Periods end at <c>StartDate.AddMonths(12k)</c>, the last one cut at maturity.</summary>
    Yearly,
}
