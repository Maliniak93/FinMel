using Application.Abstractions.Messaging;
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

public class  CreateBankAccountCommandHandler : ICommandHandler<CreateBankAccountCommand>
{
    private readonly IBankAccountRepository _bankAccountRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateBankAccountCommandHandler(IBankAccountRepository bankAccountRepository,
        IUnitOfWork unitOfWork)
    {
        _bankAccountRepository = bankAccountRepository;
        _unitOfWork = unitOfWork;
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

        var bankAccount = BankAccount.CreateInstance(request.AccountNumber,
            request.ClientNumber,
            request.ClientName,
            request.AccountName,
            request.CurrencyId,
            request.IntrestRate,
            request.AccountType);

        _bankAccountRepository.Insert(bankAccount);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
