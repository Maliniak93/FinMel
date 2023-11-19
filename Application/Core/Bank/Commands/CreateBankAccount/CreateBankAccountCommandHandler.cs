using Application.Abstractions.Messaging;
using Application.Common;
using Domain.Common;
using Domain.Entities.Bank;
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

        _bankAccountRepository.Insert(bankAccount);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
