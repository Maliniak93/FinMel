using Application.Abstractions.Messaging;
using Application.Common;
using Application.Core.Bank.Dtos;
using AutoMapper;
using Domain.Common;
using Domain.Errors;
using Domain.Repositories;

namespace Application.Core.Bank.Queries.GetStatementById;

public record GetStatementByIdQuery(int Id
    ) : IQuery<GetStatementByIdDto>;

public class GetStatementByIdQueryHandler : IQueryHandler<GetStatementByIdQuery, GetStatementByIdDto>
{
    private readonly IBankStatementRepository _repository;
    private readonly IUser _user;
    private readonly IMapper _mapper;

    public GetStatementByIdQueryHandler(IBankStatementRepository repository,
        IUser user,
        IMapper mapper)
    {
        _repository = repository;
        _user = user;
        _mapper = mapper;
    }

    public async Task<Result<GetStatementByIdDto>> Handle(GetStatementByIdQuery request, CancellationToken cancellationToken)
    {
        var bankStatement = await _repository.GetByIdAsync(request.Id,
            // ReSharper disable once AssignNullToNotNullAttribute
            _user.Id,
            true);

        if (bankStatement is null)
        {
            return Result.Failure<GetStatementByIdDto>(DomainErrors.BankStatement.BankStatementWithIdIsNotExist(request.Id));
        }

        var bankStatementDto = _mapper.Map<GetStatementByIdDto>(bankStatement);

        return bankStatementDto;
    }
}
