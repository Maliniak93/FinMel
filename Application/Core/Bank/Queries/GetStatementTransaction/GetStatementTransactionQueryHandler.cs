using Application.Abstractions.Messaging;
using Application.Common;
using Application.Core.Bank.Dtos;
using AutoMapper;
using Domain;
using Domain.Common;
using Domain.Specifications.TransactionSpecification;

namespace Application.Core.Bank.Queries.GetStatementTransaction;

public record GetStatementTransactionQuery(BankStatementsTransactionsSpecificationParameters SpecParameters,
    int PageNumber = 1,
    int PageSize = 10) : IQuery<PagedList<GetStatementsTransactionsDto>>;
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

        var statementTransactiontsQuery = await _repository.GetAllStatementTransactionsWithSpec(_user.Id, spec);

        var totalCount = statementTransactiontsQuery.Count();

        var statementTransactionts = statementTransactiontsQuery
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize);

        var statementTransactionsDto = _mapper.Map<List<GetStatementsTransactionsDto>>(statementTransactionts);

        var result = new PagedList<GetStatementsTransactionsDto>(statementTransactionsDto,
            totalCount,
            request.PageNumber,
            request.PageSize);

        return result;
    }
}


