namespace Application.Core.Dashboard.Queries.MainDashboard;

public class GetMainDashboardQueryDto
{
    public decimal PersonalWealth { get; set; }
    public decimal MonthlyExpenses { get; set; }
    public decimal AverageMonthlyExpense { get; set; }
    public decimal MonthlyIncome { get; set; }
    public decimal AverageMonthlyIncome { get; set; }
    public DateTime From { get; set; }
    public DateTime To { get; set; }
}
