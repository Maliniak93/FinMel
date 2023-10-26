namespace WebApi.Contracts.BankStatements;

public record GetStatementByIdRequest(int PageNumber,
    int PageSize);
