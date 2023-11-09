using AutoMapper;
using Domain.Entities.Bank;

namespace Application.Core.Bank.Dtos;
public class GetStatementDto
{
    public int Id { get; set; }
    public string StatementNumber { get; set; }
    public DateTime StatementFrom { get; set; }
    public DateTime StatementTo { get; set; }
    public int BankAccountId { get; set; }
    public string BankAccountName { get; set; }

    private class Mapping : Profile
    {
        public Mapping()
        {
            CreateMap<BankStatement, GetStatementDto>()
                .ForMember(d => d.BankAccountName, o => o.MapFrom(s => s.BankAccount.AccountName));
        }
    }
}
