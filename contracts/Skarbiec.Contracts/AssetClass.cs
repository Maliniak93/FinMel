namespace Skarbiec.Contracts;

// The ints are stored: keep them pinned.
public enum AssetClass
{
    Cash = 0,
    Deposit = 1,
    Stock = 2,
    Etf = 3,
    Bond = 4,
    PreciousMetal = 6,
    BankAccount = 9,
}
