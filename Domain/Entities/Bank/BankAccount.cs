using Domain.Common;
using Domain.Entities.Common;
using Domain.Entities.Dashboard;
using Domain.Enums;

namespace Domain.Entities.Bank;
public class BankAccount : BaseAuditableEntity
{
    private readonly List<BankStatement> _bankStatements = new();
    private readonly List<History> _histories = new();
    public BankAccount(
        string accountNumber,
        string clientNumber,
        string clientName,
        string accountName,
        int currencyId,
        double intrestRate,
        AccountType accountType
        )
    {
        AccountNumber = accountNumber;
        ClientNumber = clientNumber;
        ClientName = clientName;
        AccountName = accountName;
        CurrencyId = currencyId;
        IntrestRate = intrestRate;
        AccountType = accountType;
    }
    public string AccountNumber { get; private set; }
    public string ClientNumber { get; private set; }
    public string ClientName { get; private set; }
    public string AccountName { get; private set; }
    public Currency Currency { get; private set; }
    public int CurrencyId { get; private set; }
    public double IntrestRate { get; private set; }
    public AccountType AccountType { get; set; }
    public IReadOnlyCollection<BankStatement> BankStatements => _bankStatements;
    public IReadOnlyCollection<History> History => _histories;
    public MainDashboard MainDashboard { get; private set; }
    public int MainDashboardId { get; private set; }

    //public void InitializeBankHistory()
    //{
    //    History history = new History(DateTime.Now,
    //        0,
    //        this.Id);

    //    _histories.Add(history);
    //}

    public void AddBankStatement(BankStatement bankStatement)
    {
        _bankStatements.Add(bankStatement);
    }

    public void AddBankHistory(History history)
    {
        _histories.Add(history);
    }

    public void AddNewMainDashboard(MainDashboard mainDashboard)
    {
        MainDashboard = mainDashboard;
    }

    public void AddExistingDashboard(int id)
    {
        MainDashboardId = id;
    }

    public void UpdateBankAccount(string clientName,
    string accountName,
    double intrestRate)
    {
        if (!string.IsNullOrWhiteSpace(clientName))
            ClientName = clientName;

        if (!string.IsNullOrWhiteSpace(accountName))
            AccountName = accountName;

        IntrestRate = intrestRate;
    }

    public decimal CountPersonalWealth() =>
        History
        .OrderByDescending(d => d.Date)
        .Select(b => b.Balance)
        .FirstOrDefault();

    public decimal CountPersonalWealthLastMonth() =>
        History
        .OrderByDescending(d => d.Date)
        .Select(b => b.Balance)
        .Skip(1)
        .FirstOrDefault();

    public decimal CountMonthlyExpenses() =>
        BankStatements
        .OrderByDescending(d => d.StatementTo)
        .FirstOrDefault()
        .StatementTransactions
        .Where(i => i.TransactionCode.IsExpenseIncome == true)
        .Where(v => v.Value < 0)
        .Select(e => e.Value)
        .Sum();

    public decimal CountMonthlyExpensesLastMonth() =>
        BankStatements
        .OrderByDescending(d => d.StatementTo)
        .Skip(1)
        .FirstOrDefault()
        .StatementTransactions
        .Where(i => i.TransactionCode.IsExpenseIncome == true)
        .Where(v => v.Value < 0)
        .Select(e => e.Value)
        .Sum();

    public decimal AverageMonthlyExpense()
    {
        decimal totalExpense = 0;
        int month = 0;

        foreach (var bankStatement in BankStatements)
        {
            var monthlyExpense = bankStatement
                .StatementTransactions
                .Where(i => i.TransactionCode.IsExpenseIncome == true)
                .Where(v => v.Value < 0)
                .Select(e => e.Value)
                .Sum();
            totalExpense += monthlyExpense;

            month++;

            if (month == 12)
                break;
        }

        return totalExpense / month;
    }

    public decimal CountMonthlyIncome() =>
        BankStatements
        .OrderByDescending(d => d.StatementTo)
        .FirstOrDefault()
        .StatementTransactions
        .Where(i => i.TransactionCode.IsExpenseIncome == true)
        .Where(v => v.Value > 0)
        .Select(e => e.Value)
        .Sum();

    public decimal CountMonthlyIncomeLastMonth() =>
        BankStatements
        .OrderByDescending(d => d.StatementTo)
        .Skip(1)
        .FirstOrDefault()
        .StatementTransactions
        .Where(i => i.TransactionCode.IsExpenseIncome == true)
        .Where(v => v.Value > 0)
        .Select(e => e.Value)
        .Sum();

    public decimal AverageMonthlyIncome()
    {
        decimal totalIncome = 0;
        int month = 0;

        foreach (var bankStatement in BankStatements)
        {
            var monthlyIncome = bankStatement
                .StatementTransactions
                .Where(i => i.TransactionCode.IsExpenseIncome == true)
                .Where(v => v.Value > 0)
                .Select(e => e.Value)
                .Sum();
            totalIncome += monthlyIncome;

            month++;

            if (month == 12)
                break;
        }

        return totalIncome / month;
    }
}
