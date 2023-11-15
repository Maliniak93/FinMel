using AutoMapper;
using Domain.Entities.Bank;
using Domain.Entities.Files;
using Domain.Enums;
using System.Globalization;

namespace Application.Core.Bank.Dtos;
public class CreateByFileStatementCommandDto
{
    public StatementFile StatementFile { get; set; }
    public string StatementNumber { get; set; }
    public DateTime StatementFrom { get; set; }
    public decimal BeginValue { get; set; }
    public DateTime StatementTo { get; set; }
    public decimal EndValue { get; set; }
    public List<StatementTransactionsCreateByFileDto> StatementTransactions { get; set; }
    public string BankAccountNumber { get; set; }

    public class StatementTransactionsCreateByFileDto
    {
        public DateTime ExecutionDate { get; set; }
        public DateTime TransactionDate { get; set; }
        public decimal Value { get; set; }
        public decimal AccountValue { get; set; }
        public decimal RealValue { get; set; }
        public string DescriptionBase { get; set; }
        public string DescriptionOptional { get; set; }
        public string TransactionCode { get; set; }
    }

    private class Mapping : Profile
    {
        public Mapping()
        {
            CreateMap<Statement, CreateByFileStatementCommandDto>()
                .ForMember(d => d.StatementNumber, o => o.MapFrom(s => s.StatementInfo.StatementNo))
                .ForMember(d => d.StatementFrom, o => o.MapFrom(s => DateTime.ParseExact(s.StatementInfo.Begin, "dd/MM/yyyy", CultureInfo.InvariantCulture)))
                .ForMember(d => d.BeginValue, o => o.MapFrom(s => ParseDecimal(s.StatementInfo.BeginValue)))
                .ForMember(d => d.StatementTo, o => o.MapFrom(s => DateTime.ParseExact(s.StatementInfo.End, "dd/MM/yyyy", CultureInfo.InvariantCulture)))
                .ForMember(d => d.EndValue, o => o.MapFrom(s => ParseDecimal(s.StatementInfo.EndValue)))
                .ForMember(d => d.StatementTransactions, o => o.MapFrom(s => s.Transactions.TransactionList))
                .ForMember(d => d.BankAccountNumber, o => o.MapFrom(s => s.Account.Iban));

            CreateMap<Transaction, StatementTransactionsCreateByFileDto>()
                .ForMember(d => d.ExecutionDate, o => o.MapFrom(s => DateTime.ParseExact(s.ExecutionDate, "dd/MM/yyyy", CultureInfo.InvariantCulture)))
                .ForMember(d => d.TransactionDate, o => o.MapFrom(s => DateTime.ParseExact(s.CreationDate, "dd/MM/yyyy", CultureInfo.InvariantCulture)))
                .ForMember(d => d.Value, o => o.MapFrom(s => ParseDecimal(s.Value)))
                .ForMember(d => d.AccountValue, o => o.MapFrom(s => ParseDecimal(s.AccountValue)))
                .ForMember(d => d.RealValue, o => o.MapFrom(s => ParseDecimal(s.RealValue)))
                .ForMember(d => d.DescriptionBase, o => o.MapFrom(s => s.DescriptionBase))
                .ForMember(d => d.DescriptionOptional, o => o.MapFrom(s => s.DescriptionOpt))
                .ForMember(d => d.TransactionCode, o => o.MapFrom(s => s.TransactionCode));

            CreateMap<StatementTransactionsCreateByFileDto, StatementTransaction>()
                .ForMember(d => d.TransactionCode, o => o.MapFrom((src, dest, destMember, context) =>
                {
                    var codesList = (List<TransactionCode>)context.Items["codes"];
                    var code = codesList.FirstOrDefault(x => x.Code == src.TransactionCode);
                    if (code != null)
                    {
                        return code;
                    }
                    else
                    {
                        TransactionCode newCode = new TransactionCode(src.TransactionCode, src.DescriptionBase, TransactionTypes.Other);
                        codesList.Add(newCode);
                        return newCode;
                    }
                }));
        }

        private decimal ParseDecimal(string inputDecimal)
        {
            if (decimal.TryParse(inputDecimal, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
            {
                return result;
            }

            throw new ArgumentException("Invalid decimal format");
        }
    }
}

