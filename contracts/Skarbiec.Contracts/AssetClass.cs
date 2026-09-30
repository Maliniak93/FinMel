namespace Skarbiec.Contracts;

public enum AssetClass
{
    Cash,
    Deposit,
    Stock,
    Etf,
    Bond,
    Crypto,
    PreciousMetal,
    RealEstate,
    Other,

    /// <summary>A savings account (savings-accounts) — currency-valued, its terms in Portfolio's <c>SavingsAccount</c>. Appended so the stored ints stay stable.</summary>
    Savings,
}
