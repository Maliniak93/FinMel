using Application.Abstractions.Messaging;
using Application.Common;
using Application.Core.Bank.Dtos;
using AutoMapper;
using Domain;
using Domain.Common;
using Domain.Errors;

namespace Application.Core.Bank.Queries.GetStatementById;

public record GetStatementByIdQuery(int Id,
    int PageNumber = 1,
    int PageSize = 10) : IQuery<GetStatementByIdDto>;

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
            _user.Id,
            true);

        if (bankStatement is null)
        {
            return Result.Failure<GetStatementByIdDto>(DomainErrors.BankStatement.BankStatementWithIdIsNotExist(request.Id));
        }

        var totalCount = bankStatement.StatementTransactions.Count;
        var bankStatementDto = _mapper.Map<GetStatementByIdDto>(bankStatement);

        if (totalCount > 0)
        {
            var statementSkippedTransactions = bankStatement.StatementTransactions
                .Skip((request.PageNumber - 1) * request.PageSize)
                .Take(request.PageSize);

            var statementTransactionDto = _mapper.Map<List<StatementTransactionDto>>(statementSkippedTransactions);

            var pagedStatementTransactions = new PagedList<StatementTransactionDto>(statementTransactionDto,
                totalCount,
                request.PageNumber,
                request.PageSize);

            bankStatementDto.statementTransactionDtos = pagedStatementTransactions;
        }

        return bankStatementDto;
    }
}
