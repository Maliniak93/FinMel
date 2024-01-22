using Domain.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities.Dashboard;
public class MainDashboard : BaseAuditableEntity
{
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
    [Column(TypeName = "Date")]
    public DateTime From { get; private set; }
    [Column(TypeName = "Date")]
    public DateTime To { get; private set; }


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

    public void CountAverageMonthlyExpenseAndIncome(IEnumerable<MainDashboard> mainDashboards)
    {
        var oldMainDashboards = mainDashboards.Where(x => x.From < From).ToList();

        if (oldMainDashboards.Any())
        {

            var months = oldMainDashboards.Count + 1;
            AverageMonthlyExpense = (MonthlyExpenses + oldMainDashboards.Select(x => x.MonthlyExpenses).Sum()) / months;
            AverageMonthlyIncome = (MonthlyIncome + oldMainDashboards.Select(x => x.MonthlyIncome).Sum()) / months;
        }
        else
        {
            AverageMonthlyExpense = MonthlyExpenses;
            AverageMonthlyIncome = MonthlyIncome;
        }
    }

    public void ResetMainDashboard()
    {
        PersonalWealth = 0;
        MonthlyExpenses = 0;
        MonthlyIncome = 0;
        AverageMonthlyExpense = 0;
        AverageMonthlyIncome = 0;
    }
}
