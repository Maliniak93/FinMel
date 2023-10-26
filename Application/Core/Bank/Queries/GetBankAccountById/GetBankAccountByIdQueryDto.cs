using AutoMapper;
using Domain.Entities.Bank;

namespace Application.Core.Bank.Queries.GetBankAccountById;
public class GetBankAccountByIdQueryDto
{
    public int Id { get; set; }
    public string AccountNumber { get; set; }
    public decimal Balance { get; set; }
    public string ClientNumber { get; set; }
    public string ClientName { get; set; }
    public string AccountName { get; set; }
    public string Currency { get; set; }
    public string CurrencyTag { get; set; }
    public string IntrestRate { get; set; }
    public int AccountType { get; set; }
    public IReadOnlyCollection<GetBankAccountByIdQueryDtoStatement> BankStatements { get; set; }

    public class GetBankAccountByIdQueryDtoStatement
    {
        public string StatementNumber { get; set; }
        public string StatementFrom { get; set; }
        public decimal BeginValue { get; set; }
        public string StatementTo { get; set; }
        public decimal EndValue { get; set; }
        public int BankAccountId { get; set; }
    }

    private class Mapping : Profile
    {
        public Mapping()
        {
            CreateMap<BankAccount, GetBankAccountByIdQueryDto>()
                .ForMember(d => d.Balance, o => o.MapFrom(s => Math.Round(s.History.OrderByDescending(x => x.Date).FirstOrDefault().Balance, 2)))
                .ForMember(d => d.Currency, o => o.MapFrom(s => s.Currency.CurrencyName))
                .ForMember(d => d.CurrencyTag, o => o.MapFrom(s => s.Currency.CurrencyTag))
                .ForMember(d => d.IntrestRate, o => o.MapFrom(s => $"{s.IntrestRate:F2}%"))
                .ForMember(d => d.AccountType, o => o.MapFrom(s => s.AccountType));

            CreateMap<BankStatement, GetBankAccountByIdQueryDtoStatement>()
                .ForMember(d => d.BeginValue, o => o.MapFrom(s => Math.Round(s.BeginValue, 2)))
                .ForMember(d => d.EndValue, o => o.MapFrom(s => Math.Round(s.EndValue, 2)))
                .ForMember(d => d.StatementFrom, o => o.MapFrom(s => s.StatementFrom.ToShortDateString()))
                .ForMember(d => d.StatementTo, o => o.MapFrom(s => s.StatementTo.ToShortDateString()));
        }

    }
}
