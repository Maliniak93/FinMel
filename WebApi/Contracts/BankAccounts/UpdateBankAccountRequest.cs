namespace WebApi.Contracts.BankAccounts;

public record UpdateBankAccountRequest(string ClientName,
    string AccountName,
    double IntrestRate,
    decimal Balance);
