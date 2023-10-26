using Application.Abstractions.Messaging;
using Application.Common;
using Application.Core.Bank.Dtos;
using AutoMapper;
using Domain.Common;
using Domain.Entities.Common;
using Domain.Errors;
using Domain.Repositories;

namespace Application.Core.Bank.Commands.UpdateBankAccount;

public record UpdateBankAccountCommand(int Id,
    string ClientName,
    string AccountName,
    double IntrestRate,
    decimal Balance
    ) : ICommand<GetBankAccountDto>;
public class UpdateBankAccountCommandHandler : ICommandHandler<UpdateBankAccountCommand, GetBankAccountDto>
{
    private readonly IBankAccountRepository _repository;
    private readonly IUser _user;
    private readonly IMapper _mapper;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateBankAccountCommandHandler(IBankAccountRepository repository,
        IUser user,
        IMapper mapper,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _user = user;
        _mapper = mapper;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<GetBankAccountDto>> Handle(UpdateBankAccountCommand request, CancellationToken cancellationToken)
    {
        var bankAccount = await _repository.GetByIdAsync(request.Id,
            _user.Id,
            false);

        if (bankAccount is null)
        {
            return Result.Failure<GetBankAccountDto>(DomainErrors.BankAccount.BankAccountWithIdIsNotExist(request.Id));
        }

        bankAccount.UpdateBankAccount(request.ClientName,
            request.AccountName,
            request.IntrestRate);

        bankAccount.AddBankHistory(new History(DateTime.Now, request.Balance, bankAccount.Id));

        _repository.Update(bankAccount);

        await _unitOfWork.SaveChangesAsync();

        var bankAccountDto = _mapper.Map<GetBankAccountDto>(bankAccount);

        return Result.Success(bankAccountDto);
    }
}
