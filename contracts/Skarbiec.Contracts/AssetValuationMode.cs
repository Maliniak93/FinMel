namespace Skarbiec.Contracts;

// The ints are stored: keep them pinned.
public enum AssetValuationMode
{
    // Quantity × last quote × FxRate(quote currency→PLN).
    Market = 0,

    // Quantity × FxRate(Currency→PLN), e.g. plain cash, a term deposit or a treasury bond.
    CurrencyValued = 2,
}

// A default, not a constraint: Market stays open to every class; only "no instrument" is gated by it.
public static class AssetValuationModes
{
    public static AssetValuationMode Default(AssetClass assetClass) => assetClass switch
    {
        AssetClass.Cash or AssetClass.Deposit or AssetClass.Savings or AssetClass.Bond => AssetValuationMode.CurrencyValued,
        AssetClass.Stock or AssetClass.Etf or AssetClass.PreciousMetal => AssetValuationMode.Market,
        _ => throw new ArgumentOutOfRangeException(nameof(assetClass), assetClass, "Unmapped AssetClass — add it to AssetValuationModes.Default."),
    };

    public static bool SupportsCurrencyValued(AssetClass assetClass) => Default(assetClass) == AssetValuationMode.CurrencyValued;

    public static readonly IReadOnlyList<AssetClass> CurrencyValuedClasses =
        Enum.GetValues<AssetClass>().Where(SupportsCurrencyValued).ToArray();
}
