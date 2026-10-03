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

    // Appended so the stored ints stay stable.
    Savings,
}
