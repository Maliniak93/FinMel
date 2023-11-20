using Application.Abstractions.Messaging;
using Application.Common;
using Domain;
using Domain.Common;
using Domain.Errors;
using Domain.Repositories;

namespace Application.Core.Bank.Commands.UpdateTransactionDashboardDate;

public record UpdateTransactionDashboardDateCommand(int id,
    DateTime newDate) : ICommand;
public class UpdateTransactionDashboardDateCommandHandler : ICommandHandler<UpdateTransactionDashboardDateCommand>
{
    private readonly IStatementTransactionRepository _statementTransactionRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUser _user;

    public UpdateTransactionDashboardDateCommandHandler(IStatementTransactionRepository statementTransactionRepository,
        IUnitOfWork unitOfWork,
        IUser user)
    {
        _statementTransactionRepository = statementTransactionRepository;
        _unitOfWork = unitOfWork;
        _user = user;
    }
    public async Task<Result> Handle(UpdateTransactionDashboardDateCommand request, CancellationToken cancellationToken)
    {
        var transaction = await _statementTransactionRepository.GetTransactionById(request.id, _user.Id);

        if (transaction is null)
        {
            return Result.Failure(DomainErrors.StatementTransaction.StatementTransactionWithIdNotExist(request.id));
        }

        transaction.DashboardDate = request.newDate;

        _statementTransactionRepository.Update(transaction);
        await _unitOfWork.SaveChangesAsync();

        return Result.Success();
    }
}
