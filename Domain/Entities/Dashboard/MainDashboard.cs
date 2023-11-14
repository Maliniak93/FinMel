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
        decimal averageMonthlyIncome)
    {
        PersonalWealth = personalWealth;
        MonthlyExpenses = monthlyExpenses;
        AverageMonthlyExpense = averageMonthlyExpense;
        MonthlyIncome = monthlyIncome;
        AverageMonthlyIncome = averageMonthlyIncome;
    }
    public decimal PersonalWealth { get; private set; }
    public decimal MonthlyExpenses { get; private set; }
    public decimal AverageMonthlyExpense { get; private set; }
    public decimal MonthlyIncome { get; set; }
    public decimal AverageMonthlyIncome { get; private set; }
    public IReadOnlyCollection<BankAccount> BankAccounts => _bankAccounts;

}
