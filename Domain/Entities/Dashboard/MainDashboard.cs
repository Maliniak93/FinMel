using Domain.Common;
using Domain.Entities.Bank;

namespace Domain.Entities.Dashboard;
public class MainDashboard : BaseAuditableEntity
{
    private readonly List<BankAccount> _bankAccounts = new();
    public MainDashboard(
        decimal personalWealth,
        decimal monthlyExpenses,
        decimal averageMonthlyExpense,
        decimal monthlyIncome,
        decimal averageMonthlyIncome,
        DateTime from,
        DateTime to)
    {
        PersonalWealth = personalWealth;
        MonthlyExpenses = monthlyExpenses;
        AverageMonthlyExpense = averageMonthlyExpense;
        MonthlyIncome = monthlyIncome;
        AverageMonthlyIncome = averageMonthlyIncome;
        From = from;
        To = to;
    }
    public decimal PersonalWealth { get; private set; }
    public decimal MonthlyExpenses { get; private set; }
    public decimal AverageMonthlyExpense { get; private set; }
    public decimal MonthlyIncome { get; private set; }
    public decimal AverageMonthlyIncome { get; private set; }
    public DateTime From { get; private set; }
    public DateTime To { get; private set; }
    public IReadOnlyCollection<BankAccount> BankAccounts => _bankAccounts;

    public void AddToPersonalWealth(decimal value)
    {
        PersonalWealth += value;
    }
    public void CountTotalExpenses(decimal value)
    {
        MonthlyExpenses += value;
    }
    public void CountTotalIncomes(decimal value)
    {
        MonthlyIncome += value;
    }

    public void CountAverageMonthlyExpenseAndIncome(List<MainDashboard> mainDashboards)
    {
        if (mainDashboards.Any())
        {
            var months = mainDashboards.Count();
            AverageMonthlyExpense = (MonthlyExpenses + mainDashboards.Select(x => x.MonthlyExpenses).Sum()) / months;
            AverageMonthlyIncome = (MonthlyIncome + mainDashboards.Select(x => x.MonthlyIncome).Sum()) / months;
        }
        else
        {
            AverageMonthlyExpense = MonthlyExpenses;
            AverageMonthlyIncome = MonthlyIncome;
        }
    }
}
