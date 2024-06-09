using Domain.Common;
using Domain.Entities.Common;
using Domain.Enums;

namespace Domain.Entities.Bank;
public class BankAccount : BaseAuditableEntity
{
    // ReSharper disable once CollectionNeverUpdated.Local
    private readonly List<BankStatement> _bankStatements = new();
    private readonly List<History> _histories = new();

    // ReSharper disable once NotNullOrRequiredMemberIsNotInitialized
    private BankAccount(
        string accountNumber,
        string clientNumber,
        string clientName,
        string accountName,
        int currencyId,
        double intrestRate,
        AccountType accountType
        )
    {
        AccountNumber = accountNumber;
        ClientNumber = clientNumber;
        ClientName = clientName;
        AccountName = accountName;
        CurrencyId = currencyId;
        IntrestRate = intrestRate;
        AccountType = accountType;
    }

    public static BankAccount CreateInstance(string accountNumber, string clientNumber, string clientName, string accountName, int currencyId, double intrestRate, AccountType accountType)
    {
        return new BankAccount(accountNumber, clientNumber, clientName, accountName, currencyId, intrestRate, accountType);
    }

    public string AccountNumber { get; private set; }
    public string ClientNumber { get; private set; }
    public string ClientName { get; private set; }
    public string AccountName { get; private set; }
    // ReSharper disable once UnassignedGetOnlyAutoProperty
    public Currency Currency { get; }
    public int CurrencyId { get; private set; }
    public double IntrestRate { get; private set; }
    public AccountType AccountType { get; set; }
    public IReadOnlyCollection<BankStatement> BankStatements => _bankStatements;
    public IReadOnlyCollection<History> History => _histories;

    public void AddBankHistory(History history)
    {
        _histories.Add(history);
    }

    public void UpdateBankAccount(string clientName,
    string accountName,
    double intrestRate)
    {
        if (!string.IsNullOrWhiteSpace(clientName))
            ClientName = clientName;

        if (!string.IsNullOrWhiteSpace(accountName))
            AccountName = accountName;

        IntrestRate = intrestRate;
    }
}
