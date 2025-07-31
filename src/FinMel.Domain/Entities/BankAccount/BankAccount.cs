using FinMel.Domain.Abstractions;
using FinMel.Domain.ValueObjects;
using FinMel.Domain.Entities.Currency;

namespace FinMel.Domain.Entities.BankAccount;

/// <summary>
/// Represents a bank account entity with a strongly-typed identifier.
/// </summary>
public sealed class BankAccount : Entity<BankAccountId>
{
    public string Name { get; private set; }
    public AccountNumber Number { get; init; }
    public CurrencyId CurrencyId { get; private set; }
    public decimal InterestRate { get; private set; }

    private BankAccount(BankAccountId id, string name, AccountNumber number, CurrencyId currencyId, decimal interestRate)
    {
        Id = id;
        Name = name;
        Number = number;
        CurrencyId = currencyId;
        InterestRate = interestRate;
    }

    public static BankAccount Create(string name, AccountNumber number, CurrencyId currencyId, decimal interestRate = 0)
    {
        return new BankAccount(BankAccountId.New(), name, number, currencyId, interestRate);
    }

    public Result UpdateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.None);
        }
        Name = name;
        return Result.Success();
    }

    public void UpdateCurrency(CurrencyId currencyId)
    {
        if (currencyId == default)
        {
            throw new ArgumentException("CurrencyId cannot be default.", nameof(currencyId));
        }
        CurrencyId = currencyId;
    }
    
    public void UpdateInterestRate(decimal interestRate)
    {
        if (interestRate < 0 || interestRate > 100)
        {
            throw new ArgumentException("Interest rate must be between 0 and 100.", nameof(interestRate));
        }
        InterestRate = interestRate;
    }
}
