using Domain.Enums;

namespace WebApi.Contracts.BankStatements;

public record UpdateTransactionCodeRequest(string Code,
    string Description,
    TransactionTypes Type);
