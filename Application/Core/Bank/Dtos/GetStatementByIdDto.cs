using AutoMapper;
using Domain.Entities.Bank;

namespace Application.Core.Bank.Dtos;
public class GetStatementByIdDto
{
    public int Id { get; set; }
    public string StatementNumber { get; set; }
    public DateTime StatementFrom { get; set; }
    public decimal BeginValue { get; set; }
    public DateTime StatementTo { get; set; }
    public decimal EndValue { get; set; }
    public int BankAccountId { get; set; }

    private class Mapping : Profile
    {
        public Mapping()
        {
            CreateMap<BankStatement, GetStatementByIdDto>();
        }
    }
}
