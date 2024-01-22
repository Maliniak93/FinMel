using AutoMapper;
using Domain.Entities.Bank;
// ReSharper disable All

namespace Application.Core.Bank.Queries.GetBankAccounts;
public class GetBankAccountsQueryDto
{
    public int Id { get; set; }
    public string AccountNumber { get; set; }
    public string AccountName { get; set; }
    public decimal Balance { get; set; }
    public string CurrencyTag { get; set; }
    public int AccountType { get; set; }

    private class Mapping : Profile
    {
        public Mapping()
        {
            CreateMap<BankAccount, GetBankAccountsQueryDto>()
                .ForMember(d => d.CurrencyTag, o => o.MapFrom(s => s.Currency.CurrencyTag))
                .ForMember(d => d.Balance, o => o.MapFrom(s => Math.Round(s.History.OrderByDescending(x => x.Date).FirstOrDefault().Balance, 2)))
                .ForMember(d => d.AccountType, o => o.MapFrom(s => s.AccountType));
        }
    }
}
