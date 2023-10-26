using Application.Abstractions.Messaging;
using Application.Common;
using AutoMapper;
using Domain.Common;
using Domain.Errors;
using Domain.Repositories;

namespace Application.Core.Bank.Queries.GetBankAccountById;
public record GetBankAccountByIdQuery(int Id) : IQuery<GetBankAccountByIdQueryDto>;

public class GetBankAccountByIdQueryHandler : IQueryHandler<GetBankAccountByIdQuery, GetBankAccountByIdQueryDto>
{
    private readonly IBankAccountRepository _reposiory;
    private readonly IUser _user;
    private readonly IMapper _mapper;

    public GetBankAccountByIdQueryHandler(IBankAccountRepository reposiory,
        IUser user,
        IMapper mapper)
    {
        _reposiory = reposiory;
        _user = user;
        _mapper = mapper;
    }
    public async Task<Result<GetBankAccountByIdQueryDto>> Handle(GetBankAccountByIdQuery request, CancellationToken cancellationToken)
    {
        var bankAccount = await _reposiory.GetByIdAsync(request.Id, _user.Id, true);

        if (bankAccount is null)
        {
            return Result.Failure<GetBankAccountByIdQueryDto>(DomainErrors.BankAccount.BankAccountWithIdIsNotExist(request.Id));
        }

        return _mapper.Map<GetBankAccountByIdQueryDto>(bankAccount);
    }
}
