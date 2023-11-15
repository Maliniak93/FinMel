using Domain.Common;

namespace Domain.Entities.Common;
public class Investment : BaseAuditableEntity
{
    public Investment(
        decimal amount,
        int currencyId,
        DateTime startInvestment)
    {
        Amount = amount;
        CurrencyId = currencyId;
        StartInvestment = startInvestment;
    }
    public decimal Amount { get; private set; }
    public Currency Currency { get; private set; }
    public int CurrencyId { get; private set; }
    public DateTime StartInvestment { get; private set; }
    public bool IsFinished { get; private set; } = false;
    public DateTime StopInvestment { get; private set; }
    public decimal FinalAmount { get; private set; }
    public double ReturnOfInvestment { get; private set; }

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
