using Application.Abstractions.Messaging;
using Application.Common;
using Domain.Common;
using Domain.Enums;
using Domain.Errors;
using Domain.Repositories;

namespace Application.Core.Bank.Commands.UpdateTransactionCode;

public record UpdateTransactionCodeCommand(int Id,
    string Code,
    string Description,
    TransactionTypes Type) : ICommand;
public class UpdateTransactionCodeCommandHandler : ICommandHandler<UpdateTransactionCodeCommand>
{
    private readonly ITransactionCodeRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUser _user;

    public UpdateTransactionCodeCommandHandler(ITransactionCodeRepository repository, IUnitOfWork unitOfWork, IUser user)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _user = user;
    }

    public async Task<Result> Handle(UpdateTransactionCodeCommand request, CancellationToken cancellationToken)
    {
        var code = await _repository.GetByIdAsync(request.Id, _user.Id, false);

        if (code is null)
        {
            return Result.Failure(DomainErrors.TransactionCode.TransactionCodeWithIdIsNotExist(request.Id));
        }

        code.UpdateTransactionCode(request.Code,
            request.Description,
            request.Type);

        _repository.Update(code);

        await _unitOfWork.SaveChangesAsync();

        return Result.Success();
    }
}
