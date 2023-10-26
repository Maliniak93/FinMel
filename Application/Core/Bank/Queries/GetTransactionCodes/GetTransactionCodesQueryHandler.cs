using Application.Abstractions.Messaging;
using Application.Common;
using AutoMapper;
using Domain.Common;
using Domain.Repositories;

namespace Application.Core.Bank.Queries.GetTransactionCodes;

public record GetTransactionCodesQuery(int PageNumber = 1,
    int PageSize = 10) : IQuery<PagedList<GetTransactionCodesQueryDto>>;
public class GetTransactionCodesQueryHandler : IQueryHandler<GetTransactionCodesQuery, PagedList<GetTransactionCodesQueryDto>>
{
    private readonly ITransactionCodeRepository _repository;
    private readonly IUser _user;
    private readonly IMapper _mapper;

    public GetTransactionCodesQueryHandler(ITransactionCodeRepository repository,
        IUser user,
        IMapper mapper)
    {
        _repository = repository;
        _user = user;
        _mapper = mapper;
    }
    public async Task<Result<PagedList<GetTransactionCodesQueryDto>>> Handle(GetTransactionCodesQuery request, CancellationToken cancellationToken)
    {
        var codesQuery = await _repository.GetAllAsync(_user.Id);

        var codes = codesQuery
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize);

        var totalCount = codesQuery.Count();

        var codesDto = _mapper.Map<List<GetTransactionCodesQueryDto>>(codes);

        var result = new PagedList<GetTransactionCodesQueryDto>(codesDto,
            totalCount,
            request.PageNumber,
            request.PageSize);

        return result;
    }
}
