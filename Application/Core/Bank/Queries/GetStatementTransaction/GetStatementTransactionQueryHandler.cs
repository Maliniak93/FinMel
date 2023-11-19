using Application.Abstractions.Messaging;
using Application.Common;
using Application.Core.Bank.Dtos;
using AutoMapper;
using Domain;
using Domain.Common;
using Domain.Errors;
using Domain.Specifications.TransactionSpecification;

namespace Application.Core.Bank.Queries.GetStatementTransaction;

public record GetStatementTransactionQuery(int Id,
    BankStatementsTransactionsSpecificationParameters SpecParameters
    ) : IQuery<PagedList<GetStatementsTransactionsDto>>;
public class GetStatementTransactionQueryHandler : IQueryHandler<GetStatementTransactionQuery, PagedList<GetStatementsTransactionsDto>>
{
    private readonly IStatementTransactionRepository _repository;
    private readonly IUser _user;
    private readonly IMapper _mapper;

    public GetStatementTransactionQueryHandler(IStatementTransactionRepository repository,
        IUser user,
        IMapper mapper)
    {
        _repository = repository;
        _user = user;
        _mapper = mapper;
    }

    public async Task<Result<PagedList<GetStatementsTransactionsDto>>> Handle(GetStatementTransactionQuery request, CancellationToken cancellationToken)
    {
        var spec = new BankStatementsTransactionsSpecification(request.SpecParameters);

        var statementTransactiontsQuery = await _repository.GetStatementByIdTransactionsWithSpec(request.Id, _user.Id, spec);

        if (!statementTransactiontsQuery.Any())
        {
            return Result.Failure<PagedList<GetStatementsTransactionsDto>>(DomainErrors.BankStatement.StatementWithIdTransactionIsNotFound(request.Id));
        }

        var totalCount = statementTransactiontsQuery.Count;

        var statementTransactionts = statementTransactiontsQuery
            .Skip((request.SpecParameters.PageNumber - 1) * request.SpecParameters.PageSize)
            .Take(request.SpecParameters.PageSize);

        var statementTransactionsDto = _mapper.Map<List<GetStatementsTransactionsDto>>(statementTransactionts);

        var result = new PagedList<GetStatementsTransactionsDto>(statementTransactionsDto,
            totalCount,
            request.SpecParameters.PageNumber,
            request.SpecParameters.PageSize);

        return result;
    }
}


