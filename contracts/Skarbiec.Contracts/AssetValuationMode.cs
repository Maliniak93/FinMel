namespace Skarbiec.Contracts;

public enum AssetValuationMode
{
    // Quantity × last quote × FxRate(quote currency→PLN).
    Market,

    // ManualValueAmount × FxRate(Currency→PLN) at the snapshot date.
    Manual,

    // Quantity × FxRate(Currency→PLN), e.g. plain cash or a term deposit.
    CurrencyValued,
}

// A default, not a constraint: Market and Manual stay open to every class; only "neither field set" is gated by it.
public static class AssetValuationModes
{
    public static AssetValuationMode Default(AssetClass assetClass) => assetClass switch
    {
        AssetClass.Cash or AssetClass.Deposit or AssetClass.Savings => AssetValuationMode.CurrencyValued,
        AssetClass.Stock or AssetClass.Etf or AssetClass.Bond or AssetClass.Crypto or AssetClass.PreciousMetal => AssetValuationMode.Market,
        AssetClass.RealEstate or AssetClass.Other => AssetValuationMode.Manual,
        _ => throw new ArgumentOutOfRangeException(nameof(assetClass), assetClass, "Unmapped AssetClass — add it to AssetValuationModes.Default."),
    };

    public static bool SupportsCurrencyValued(AssetClass assetClass) => Default(assetClass) == AssetValuationMode.CurrencyValued;

    public static readonly IReadOnlyList<AssetClass> CurrencyValuedClasses =
        Enum.GetValues<AssetClass>().Where(SupportsCurrencyValued).ToArray();
}
