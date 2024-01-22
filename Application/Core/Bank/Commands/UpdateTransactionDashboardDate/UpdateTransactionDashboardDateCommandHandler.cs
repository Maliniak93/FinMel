using Application.Abstractions.Messaging;
using Application.Common;
using Domain.Common;
using Domain.Errors;
using Domain.Repositories;

namespace Application.Core.Bank.Commands.UpdateTransactionDashboardDate;

public record UpdateTransactionDashboardDateCommand(int Id,
    DateTime NewDate) : ICommand;
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
        // ReSharper disable once AssignNullToNotNullAttribute
        var transaction = await _statementTransactionRepository.GetTransactionById(request.Id, _user.Id);

        if (transaction is null)
        {
            return Result.Failure(DomainErrors.StatementTransaction.StatementTransactionWithIdNotExist(request.Id));
        }

        transaction.DashboardDate = request.NewDate;

        _statementTransactionRepository.Update(transaction);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
