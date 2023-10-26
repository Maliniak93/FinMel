using Application.Abstractions.Messaging;
using Application.Common;
using Application.Core.Bank.Dtos;
using Domain.Common;
using Domain.Errors;
using Domain.Repositories;

namespace Application.Core.Bank.Commands.DeleteBankAccount;
public record DeleteBankAccountCommand(int Id) : ICommand;
public class DeleteBankAccountCommandHandler : ICommandHandler<DeleteBankAccountCommand>
{
    private readonly IBankAccountRepository _repository;
    private readonly IUser _user;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteBankAccountCommandHandler(IBankAccountRepository repository,
        IUser user,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _user = user;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteBankAccountCommand request, CancellationToken cancellationToken)
    {
        var bankAccount = await _repository.GetByIdAsync(request.Id,
            _user.Id,
            false);

        if (bankAccount is null)
        {
            return Result.Failure<GetBankAccountDto>(DomainErrors.BankAccount.BankAccountWithIdIsNotExist(request.Id));
        }

        _repository.Remove(bankAccount);

        await _unitOfWork.SaveChangesAsync();

        return Result.Success();
    }
}
