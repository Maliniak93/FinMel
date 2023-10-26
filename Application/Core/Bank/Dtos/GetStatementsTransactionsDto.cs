using AutoMapper;
using Domain.Entities.Bank;

namespace Application.Core.Bank.Dtos;
public class GetStatementsTransactionsDto
{
    public int Id { get; set; }
    public int BankStatementId { get; set; }
    public DateTime ExecutionDate { get; set; }
    public DateTime TransactionDate { get; set; }
    public decimal Value { get; set; }
    public decimal AccountValue { get; set; }
    public decimal RealValue { get; set; }
    public string Description { get; set; }

    private class Mapping : Profile
    {
        public Mapping()
        {
            CreateMap<StatementTransaction, GetStatementsTransactionsDto>()
                .ForMember(d => d.Description, o => o.MapFrom(s => s.DescriptionBase + s.DescriptionOptional));
        }
    }
}
