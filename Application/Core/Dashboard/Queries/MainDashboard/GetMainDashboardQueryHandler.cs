using Application.Abstractions.Messaging;
using Application.Common;
using AutoMapper;
using Domain.Common;
using Domain.Errors;
using Domain.Repositories;

namespace Application.Core.Dashboard.Queries.MainDashboard;

public record GetMainDashboardQuery() : IQuery<GetMainDashboardQueryDto>;
internal class GetMainDashboardQueryHandler : IQueryHandler<GetMainDashboardQuery, GetMainDashboardQueryDto>
{
    private readonly IDashboardRepository _dashboardRepository;
    private readonly IBankAccountRepository _bankAccountRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUser _user;
    private readonly IMapper _mapper;

    public GetMainDashboardQueryHandler(IDashboardRepository dashboardRepository,
        IBankAccountRepository bankAccountRepository,
        IUnitOfWork unitOfWork,
        IUser user,
        IMapper mapper)
    {
        _dashboardRepository = dashboardRepository;
        _bankAccountRepository = bankAccountRepository;
        _unitOfWork = unitOfWork;
        _user = user;
        _mapper = mapper;
    }
    public async Task<Result<GetMainDashboardQueryDto>> Handle(GetMainDashboardQuery request, CancellationToken cancellationToken)
    {
        var userDashboard = await _dashboardRepository.GetUserDashboard(_user.Id);

        if (userDashboard is null)
        {
            return Result.Failure<GetMainDashboardQueryDto>(DomainErrors.Dashboard.MainDashboardNotExist);
        }

        //var bankAccounts = await _bankAccountRepository.GetAllWithStatementsAndTransactionsAsync(_user.Id);
        //if (!bankAccounts.Any())
        //{
        //    return Result.Failure<GetMainDashboardQueryDto>(DomainErrors.BankAccount.BankAccountsNotExist);
        //}

        //var mainDashboardQueryDto = new GetMainDashboardQueryDto();

        //foreach (var bankAccount in bankAccounts)
        //{
        //    mainDashboardQueryDto.PersonalWealth += bankAccount.CountPersonalWealth();
        //    mainDashboardQueryDto.CompPreviousMonth += bankAccount.CountPersonalWealthLastMonth();
        //    mainDashboardQueryDto.MonthlyExpenses += bankAccount.CountMonthlyExpenses();
        //    mainDashboardQueryDto.CompPreviousMonthExpenses += bankAccount.CountMonthlyExpensesLastMonth();
        //    mainDashboardQueryDto.AverageMonthlyExpense += bankAccount.AverageMonthlyExpense();
        //    mainDashboardQueryDto.MonthlyIncome += bankAccount.CountMonthlyIncome();
        //    mainDashboardQueryDto.CompPreviousMonthIncome += bankAccount.CountMonthlyIncomeLastMonth();
        //    mainDashboardQueryDto.AverageMonthlyIncome += bankAccount.AverageMonthlyIncome();
        //}
        var mainDashboardQueryDto = new GetMainDashboardQueryDto();

        return Result.Success(mainDashboardQueryDto);
    }
}