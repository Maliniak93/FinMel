namespace WebApi.Contracts.BankStatements;

public record UpdateTransactionCodeRequest(string Code,
    string Description,
    bool IsExpensionIncome);
