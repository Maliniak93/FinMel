using Application.Abstractions.Messaging;
using Application.Common;
using Domain;
using Domain.Common;
using Domain.Errors;
using Domain.Repositories;

namespace Application.Core.Bank.Commands.UpdateTransactionType;

public record UpdateTransactionTypeCommand(int Id,
    string TransactionType) : ICommand;
public class UpdateTransactionTypeCommandHandler : ICommandHandler<UpdateTransactionTypeCommand>
{
    private readonly IStatementTransactionRepository _statementTransactionRepository;
    private readonly ITransactionCodeRepository _transactionCodeRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUser _user;

    public UpdateTransactionTypeCommandHandler(IStatementTransactionRepository statementTransactionRepository,
        ITransactionCodeRepository transactionCodeRepository,
        IUnitOfWork unitOfWork,
        IUser user)
    {
        _statementTransactionRepository = statementTransactionRepository;
        _transactionCodeRepository = transactionCodeRepository;
        _unitOfWork = unitOfWork;
        _user = user;
    }
    public async Task<Result> Handle(UpdateTransactionTypeCommand request, CancellationToken cancellationToken)
    {
        var transaction = await _statementTransactionRepository.GetTransactionById(request.Id, _user.Id);

        if (transaction is null)
        {
            return Result.Failure(DomainErrors.StatementTransaction.StatementTransactionWithIdNotExist(request.Id));
        }

        var codes = await _transactionCodeRepository.GetAllAsync(_user.Id);
        if (!codes.Any())
        {
            return Result.Failure(DomainErrors.TransactionCode.TransactionCodesIsNotExist);
        }

        transaction.UpdateTransactionType(request.TransactionType, codes);

        _statementTransactionRepository.Update(transaction);
        await _unitOfWork.SaveChangesAsync();

        return Result.Success();
    }
}
