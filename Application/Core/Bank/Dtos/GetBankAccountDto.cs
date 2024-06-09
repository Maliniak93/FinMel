using AutoMapper;
using Domain.Entities.Bank;
// ReSharper disable All

namespace Application.Core.Bank.Dtos;

public class GetBankAccountDto
{
    public int Id { get; set; }
    public string AccountNumber { get; set; }
    public decimal Balance { get; set; }
    public string ClientNumber { get; set; }
    public string ClientName { get; set; }
    public string AccountName { get; set; }
    public string Currency { get; set; }
    public double IntrestRate { get; set; }

    private class Mapping : Profile
    {
        public Mapping()
        {
            CreateMap<BankAccount, GetBankAccountDto>()
                .ForMember(d => d.Currency, o => o.MapFrom(s => s.Currency.CurrencyTag));
        }
    }
}
