using Application.Abstractions.Messaging;
using Application.Common;
using AutoMapper;
using Domain.Common;
using Domain.Errors;
using Domain.Repositories;

namespace Application.Core.Bank.Queries.GetBankAccounts;

public record GetBankAccountsQuery : IQuery<IEnumerable<GetBankAccountsQueryDto>>;

public class GetBankAccountsQueryHandlers : IQueryHandler<GetBankAccountsQuery, IEnumerable<GetBankAccountsQueryDto>>
{
    private readonly IBankAccountRepository _repository;
    private readonly IUser _user;
    private readonly IMapper _mapper;
    public GetBankAccountsQueryHandlers(IBankAccountRepository repository,
        IUser user,
        IMapper mapper)
    {
        _repository = repository;
        _user = user;
        _mapper = mapper;
    }

    public async Task<Result<IEnumerable<GetBankAccountsQueryDto>>> Handle(GetBankAccountsQuery request, CancellationToken cancellationToken)
    {
        // ReSharper disable once AssignNullToNotNullAttribute
        var bankAccounts = await _repository.GetAllAsync(_user.Id);

        if (!bankAccounts.Any())
        {
            return Result.Failure<IEnumerable<GetBankAccountsQueryDto>>(DomainErrors.BankAccount.BankAccountsNotExist);
        }

        var bankAccountsDto = _mapper.Map<IEnumerable<GetBankAccountsQueryDto>>(bankAccounts);
        return Result.Success(bankAccountsDto);
    }
}
