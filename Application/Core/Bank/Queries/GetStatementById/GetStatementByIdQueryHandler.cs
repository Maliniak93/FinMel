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
    int PageSize = 10) : IQuery<Tuple<GetStatementByIdDto, PagedList<StatementTransactionDto>>>;

public class GetStatementByIdQueryHandler : IQueryHandler<GetStatementByIdQuery, Tuple<GetStatementByIdDto, PagedList<StatementTransactionDto>>>
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

    public async Task<Result<Tuple<GetStatementByIdDto, PagedList<StatementTransactionDto>>>> Handle(GetStatementByIdQuery request, CancellationToken cancellationToken)
    {
        var bankStatement = await _repository.GetByIdAsync(request.Id,
            _user.Id,
            true);

        if (bankStatement is null)
        {
            return Result.Failure<Tuple<GetStatementByIdDto, PagedList<StatementTransactionDto>>>(DomainErrors.BankStatement.BankStatementWithIdIsNotExist(request.Id));
        }

        var totalCount = bankStatement.StatementTransactions.Count;
        var bankStatementDto = _mapper.Map<GetStatementByIdDto>(bankStatement);
        PagedList<StatementTransactionDto> pagedStatementTransactions = null;

        if (totalCount > 0)
        {
            var statementSkippedTransactions = bankStatement.StatementTransactions
                .Skip((request.PageNumber - 1) * request.PageSize)
                .Take(request.PageSize);

            var statementTransactionDto = _mapper.Map<List<StatementTransactionDto>>(statementSkippedTransactions);

            pagedStatementTransactions = new PagedList<StatementTransactionDto>(statementTransactionDto,
                totalCount,
                request.PageNumber,
                request.PageSize);

            bankStatementDto.statementTransactionDtos = pagedStatementTransactions.Items;
        }

        return new Tuple<GetStatementByIdDto, PagedList<StatementTransactionDto>>(bankStatementDto, pagedStatementTransactions);
    }
}
