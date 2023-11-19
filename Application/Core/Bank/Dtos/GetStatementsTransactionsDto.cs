using AutoMapper;
using Domain.Entities.Bank;

namespace Application.Core.Bank.Dtos;
public class GetStatementsTransactionsDto
{
    public int Id { get; set; }
    public int BankStatementId { get; set; }
    public DateTime ExecutionDate { get; set; }
    public decimal Value { get; set; }
    public decimal RealValue { get; set; }
    public string Description { get; set; }
    public string Type { get; set; }
    public string TransactionCode { get; set; }

    private class Mapping : Profile
    {
        public Mapping()
        {
            CreateMap<StatementTransaction, GetStatementsTransactionsDto>()
                .ForMember(d => d.Description, o => o.MapFrom(s => s.DescriptionBase + s.DescriptionOptional))
                .ForMember(d => d.Type, o => o.MapFrom(s => s.TransactionCode.Type))
                .ForMember(d => d.TransactionCode, o => o.MapFrom(s => s.TransactionCode.Code));

        }
    }
}
