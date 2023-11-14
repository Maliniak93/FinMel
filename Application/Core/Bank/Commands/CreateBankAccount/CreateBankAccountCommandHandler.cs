using Application.Abstractions.Messaging;
using Application.Common;
using Domain.Common;
using Domain.Entities.Bank;
using Domain.Entities.Dashboard;
using Domain.Enums;
using Domain.Errors;
using Domain.Repositories;

namespace Application.Core.Bank.Commands.CreateBankAccount;

public record CreateBankAccountCommand(string AccountNumber,
    decimal Balance,
    string ClientNumber,
    string ClientName,
    string AccountName,
    int CurrencyId,
    double IntrestRate,
    AccountType AccountType
    ) : ICommand;

public class CreateBankAccountCommandHandler : ICommandHandler<CreateBankAccountCommand>
{
    private readonly IBankAccountRepository _bankAccountRepository;
    private readonly IDashboardRepository _dashboardRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUser _user;

    public CreateBankAccountCommandHandler(IBankAccountRepository bankAccountRepository,
        IDashboardRepository dashboardRepository,
        IUnitOfWork unitOfWork,
        IUser user)
    {
        _bankAccountRepository = bankAccountRepository;
        _dashboardRepository = dashboardRepository;
        _unitOfWork = unitOfWork;
        _user = user;
    }

    public async Task<Result> Handle(CreateBankAccountCommand request, CancellationToken cancellationToken)
    {
        if (!await _bankAccountRepository.IsAcountNumberUniqueAsync(request.AccountNumber))
        {
            return Result.Failure(DomainErrors.BankAccount.BankAccountNumberIsNotUnique);
        }

        if (!await _bankAccountRepository.IsCurrencyExistAsync(request.CurrencyId))
        {
            return Result.Failure(DomainErrors.BankAccount.BankAccountCurrencyIsNotExist);
        }

        var bankAccount = new BankAccount(
            request.AccountNumber,
            request.ClientNumber,
            request.ClientName,
            request.AccountName,
            request.CurrencyId,
            request.IntrestRate,
            request.AccountType
            );

        if (await _bankAccountRepository.IsFirstBankAccount(_user.Id))
        {
            var dashboard = new MainDashboard(0, 0, 0, 0, 0);
            _dashboardRepository.Insert(dashboard);
            bankAccount.AddNewMainDashboard(dashboard);
        }
        else
        {
            var dashboard = await _dashboardRepository.GetUserDashboard(_user.Id);

            if (dashboard == null)
            {
                return Result.Failure(DomainErrors.BankAccount.MainDashboardError);
            }

            bankAccount.AddExistingDashboard(dashboard.Id);
        }

        _bankAccountRepository.Insert(bankAccount);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
