using Application.Abstractions.Messaging;
using Application.Common;
using Domain;
using Domain.Common;
using Domain.Entities.Dashboard;
using Domain.Enums;
using Domain.Errors;
using Domain.Repositories;

namespace Application.Core.Dashboard.Commands.CreateMainDashboard;

public record CreateMainDashboardCommand(
    int year,
    int month) : ICommand;
public class CreateMainDashboardCommandHandler : ICommandHandler<CreateMainDashboardCommand>
{
    private readonly IDashboardRepository _dashboardRepository;
    private readonly IInvestmentRepository _investmentRepository;
    private readonly IStatementTransactionRepository _statementTransactionRepository;
    private readonly IBankAccountRepository _bankAccountRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUser _user;

    public CreateMainDashboardCommandHandler(IDashboardRepository dashboardRepository,
        IInvestmentRepository investmentRepository,
        IStatementTransactionRepository statementTransactionRepository,
        IBankAccountRepository bankAccountRepository,
        IUnitOfWork unitOfWork,
        IUser user)
    {
        _dashboardRepository = dashboardRepository;
        _investmentRepository = investmentRepository;
        _statementTransactionRepository = statementTransactionRepository;
        _bankAccountRepository = bankAccountRepository;
        _unitOfWork = unitOfWork;
        _user = user;
    }
    public async Task<Result> Handle(CreateMainDashboardCommand request, CancellationToken cancellationToken)
    {
        var firstDayOfMonth = new DateTime(request.year, request.month, 1);
        var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);

        var mainDashboards = await _dashboardRepository.GetAllAsync(_user.Id);
        if (mainDashboards.Any(m => m.From == firstDayOfMonth && m.To == lastDayOfMonth))
        {
            return Result.Failure(DomainErrors.Dashboard.MainDashboardForTimeRangeAlreadyExist(firstDayOfMonth, lastDayOfMonth));
        }

        MainDashboard mainDashboard = new(0, 0, 0, 0, 0, firstDayOfMonth, lastDayOfMonth);

        var investments = await _investmentRepository.GetAllInTimeRange(_user.Id, firstDayOfMonth, lastDayOfMonth);
        if (investments.Any())
        {
            mainDashboard.AddToPersonalWealth(investments.Select(a => a.Amount).Sum());
        }

        var bankAccounts = await _bankAccountRepository.GetAllAsync(_user.Id);
        if (bankAccounts.Any())
        {
            foreach (var bankAccount in bankAccounts)
            {
                var transactions = await _statementTransactionRepository.GetStatementsTransactionsInTimeRange(_user.Id, firstDayOfMonth, lastDayOfMonth, bankAccount.Id);
                if (transactions.Any())
                {
                    mainDashboard.AddToPersonalWealth(transactions.FirstOrDefault().AccountValue);

                    var expenses = transactions.Where(x => x.TransactionCode.Type == TransactionTypes.Expenses).Select(x => x.Value).Sum();
                    mainDashboard.CountTotalExpenses(expenses);

                    var incomes = transactions.Where(x => x.TransactionCode.Type == TransactionTypes.Income).Select(x => x.Value).Sum();
                    mainDashboard.CountTotalIncomes(incomes);
                }
            }
        }

        mainDashboard.CountAverageMonthlyExpenseAndIncome(mainDashboards);

        _dashboardRepository.Insert(mainDashboard);
        await _unitOfWork.SaveChangesAsync();

        return Result.Success();
    }
}
