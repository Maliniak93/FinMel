using Application.Abstractions.Messaging;
using Application.Common;
using Domain.Common;
using Domain.Enums;
using Domain.Errors;
using Domain.Repositories;

namespace Application.Core.Dashboard.Commands.UpdateMainDashboard;

public record UpdateMainDashboardCommand(
    int MainDashboardId) : ICommand;
public class UpdateMainDashboardCommandHandler : ICommandHandler<UpdateMainDashboardCommand>
{
    private readonly IDashboardRepository _dashboardRepository;
    private readonly IInvestmentRepository _investmentRepository;
    private readonly IStatementTransactionRepository _statementTransactionRepository;
    private readonly IBankAccountRepository _bankAccountRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUser _user;

    public UpdateMainDashboardCommandHandler(IDashboardRepository dashboardRepository,
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
    public async Task<Result> Handle(UpdateMainDashboardCommand request, CancellationToken cancellationToken)
    {
        // ReSharper disable once AssignNullToNotNullAttribute
        var mainDashboard = await _dashboardRepository.GetByIdAsync(_user.Id, request.MainDashboardId);
        if (mainDashboard is null)
        {
            return Result.Failure(DomainErrors.Dashboard.MainDashboardWithIdNotExist(request.MainDashboardId));
        }

        mainDashboard.ResetMainDashboard();

        var mainDashboards = await _dashboardRepository.GetAllAsync(_user.Id);

        var investments = await _investmentRepository.GetAllInTimeRange(_user.Id, mainDashboard.From, mainDashboard.To);
        if (investments.Any())
        {
            mainDashboard.AddToPersonalWealth(investments.Select(a => a.Amount).Sum());
        }

        var bankAccounts = await _bankAccountRepository.GetAllAsync(_user.Id);
        if (bankAccounts.Any())
        {
            foreach (var bankAccount in bankAccounts)
            {
                var transactions = await _statementTransactionRepository.GetStatementsTransactionsInTimeRange(_user.Id, mainDashboard.From, mainDashboard.To, bankAccount.Id);
                if (transactions.Any())
                {
                    mainDashboard.AddToPersonalWealth(transactions.FirstOrDefault()!.AccountValue);

                    var expenses = transactions.Where(x => x.TransactionCode.Type == TransactionTypes.Expenses).Select(x => x.Value).Sum();
                    mainDashboard.CountTotalExpenses(expenses);

                    var incomes = transactions.Where(x => x.TransactionCode.Type == TransactionTypes.Income).Select(x => x.Value).Sum();
                    mainDashboard.CountTotalIncomes(incomes);
                }
            }
        }

        mainDashboard.CountAverageMonthlyExpenseAndIncome(mainDashboards);

        _dashboardRepository.Update(mainDashboard);
        await _unitOfWork.SaveChangesAsync();

        return Result.Success();
    }
}
