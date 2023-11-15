using Domain.Common;

namespace Domain.Entities.Common;
public class Investment : BaseAuditableEntity
{
    public Investment(
        decimal amount,
        int currencyId,
        DateTime startInvestment)
    {
        Amount = Math.Abs(amount);
        CurrencyId = currencyId;
        StartInvestment = startInvestment;
    }
    public decimal Amount { get; private set; }
    public Currency Currency { get; private set; }
    public int CurrencyId { get; private set; }
    public DateTime StartInvestment { get; private set; }
    public bool IsFinished { get; private set; } = false;
    public DateTime? StopInvestment { get; private set; } = null;
    public decimal? FinalAmount { get; private set; } = null;
    public double? ReturnOfInvestment { get; private set; } = null;

    public void FinishInvestment(DateTime stopInvestment, decimal finalAmount, decimal returnOfInvestment)
    {
        IsFinished = true;
        StopInvestment = stopInvestment;
        FinalAmount = finalAmount;
        CountReturnOfInvestment(returnOfInvestment);

    }

    private void CountReturnOfInvestment(decimal returnOfInvestment)
    {
        ReturnOfInvestment = (double)((returnOfInvestment - Amount) / Amount) * 100;
    }
}
