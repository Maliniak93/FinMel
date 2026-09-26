namespace Skarbiec.Portfolio.Data;

/// <summary>The unit of a term deposit's <see cref="TermDeposit.TermLength"/> (term-deposits).</summary>
public enum DepositTermUnit
{
    /// <summary>Maturity is <c>StartDate + n</c> calendar days.</summary>
    Days,

    /// <summary>Maturity is <c>StartDate.AddMonths(n)</c>, clamped to the month's end.</summary>
    Months,
}
