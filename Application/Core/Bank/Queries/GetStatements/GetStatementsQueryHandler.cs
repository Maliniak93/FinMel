using Application.Abstractions.Messaging;
using Application.Common;
using Application.Core.Bank.Dtos;
using AutoMapper;
using Domain;
using Domain.Common;
using Domain.Specifications.StatementSpecification;

namespace Application.Core.Bank.Queries.GetStatements;

public record GetStatementsQuery(BankStatementsSpecificationParameters SpecParameters,
    int PageNumber = 1,
    int PageSize = 10) : IQuery<PagedList<GetStatementDto>>;
public class GetStatementsQueryHandler : IQueryHandler<GetStatementsQuery, PagedList<GetStatementDto>>
{
    private readonly IBankStatementRepository _repository;
    private readonly IUser _user;
    private readonly IMapper _mapper;
    public GetStatementsQueryHandler(IBankStatementRepository repository,
        IUser user,
        IMapper mapper)
    {
        _repository = repository;
        _user = user;
        _mapper = mapper;
    }
    public async Task<Result<PagedList<GetStatementDto>>> Handle(GetStatementsQuery request, CancellationToken cancellationToken)
    {
        var spec = new BankStatementsSpecification(request.SpecParameters);

        var bankStatementsQuery = await _repository.GetAllStatementsWithSpec(_user.Id, spec);

        var totalCount = bankStatementsQuery.Count();

        var bankStatements = bankStatementsQuery
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize);

        var bankStatementsDto = _mapper.Map<List<GetStatementDto>>(bankStatements);

        var result = new PagedList<GetStatementDto>(bankStatementsDto,
            totalCount,
            request.PageNumber,
            request.PageSize);

        return result;
    }
}
