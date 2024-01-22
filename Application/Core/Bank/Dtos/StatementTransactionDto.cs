using AutoMapper;
using Domain.Entities.Bank;
// ReSharper disable All

namespace Application.Core.Bank.Dtos;

public class StatementTransactionDto
{
    public int BankStatementId { get; set; }
    public DateTime TransactionDate { get; set; }
    public decimal RealValue { get; set; }
    public string DescriptionBase { get; set; }
    public string DescriptionOptional { get; set; }

    private class Mapping : Profile
    {
        public Mapping()
        {
            CreateMap<StatementTransaction, StatementTransactionDto>();
        }
    }
}