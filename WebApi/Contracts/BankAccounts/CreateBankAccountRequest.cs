using Domain.Enums;

namespace WebApi.Contracts.BankAccounts;

public record CreateBankAccountRequest(string AccountNumber,
    decimal Balance,
    string ClientNumber,
    string ClientName,
    string AccountName,
    int CurrencyId,
    double IntrestRate,
    AccountType AccountType);

