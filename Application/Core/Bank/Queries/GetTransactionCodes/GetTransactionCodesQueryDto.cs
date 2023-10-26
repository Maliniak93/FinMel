using AutoMapper;
using Domain.Entities.Bank;

namespace Application.Core.Bank.Queries.GetTransactionCodes;

public class GetTransactionCodesQueryDto
{
    public int Id { get; set; }
    public string Code { get; set; }
    public string Description { get; set; }
    public bool IsExpenseIncome { get; set; }

    private class Mapping : Profile
    {
        public Mapping()
        {
            CreateMap<TransactionCode, GetTransactionCodesQueryDto>();
        }
    }
}