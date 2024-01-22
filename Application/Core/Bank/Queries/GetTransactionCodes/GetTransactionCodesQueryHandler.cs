using Application.Abstractions.Messaging;
using Application.Common;
using AutoMapper;
using Domain.Common;
using Domain.Repositories;
// ReSharper disable All

namespace Application.Core.Bank.Queries.GetTransactionCodes;

public record GetTransactionCodesQuery : IQuery<List<GetTransactionCodesQueryDto>>;
public class GetTransactionCodesQueryHandler : IQueryHandler<GetTransactionCodesQuery, List<GetTransactionCodesQueryDto>>
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
    public async Task<Result<List<GetTransactionCodesQueryDto>>> Handle(GetTransactionCodesQuery request, CancellationToken cancellationToken)
    {
        var codes = await _repository.GetAllAsync(_user.Id);

        var result = _mapper.Map<List<GetTransactionCodesQueryDto>>(codes);

        return result;
    }
}
