﻿using Domain.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities.Bank;
public class StatementTransaction : BaseAuditableEntity
{
    public BankStatement BankStatement { get; private set; }
    public int BankStatementId { get; private set; }
    [Column(TypeName = "Date")]
    public DateTime ExecutionDate { get; private set; }
    [Column(TypeName = "Date")]
    public DateTime TransactionDate { get; private set; }
    public decimal Value { get; private set; }
    public decimal AccountValue { get; private set; }
    public decimal RealValue { get; private set; }
    public string DescriptionBase { get; private set; }
    public string DescriptionOptional { get; private set; }
    public int TransactionCodeId { get; private set; }
    public TransactionCode TransactionCode { get; set; }

    public void AddTransactionCode(TransactionCode transactionCode)
    {
        TransactionCode = transactionCode;
    }

    public void SetTransactionCode(int transactionCodeId)
    {
        TransactionCodeId = transactionCodeId;
    }
}