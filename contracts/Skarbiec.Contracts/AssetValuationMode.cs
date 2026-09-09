namespace Skarbiec.Contracts;

/// <summary>
/// The three ways an <c>Asset</c> (Portfolio) can be valued (M1.4, 03-domain-model.md §valuation
/// algorithm). Before M1.4 this was implicit — <c>InstrumentId is not null</c> meant market,
/// otherwise manual — which silently valued a currency-only asset at 0 PLN once a third shape
/// (neither field set) became possible. Making the mode an explicit, stored property removes that
/// ambiguity: every reader (Reporting's <c>ValuationAlgorithm</c>, Portfolio's own handlers) branches
/// on this value instead of re-deriving it from which nullable fields happen to be populated.
/// </summary>
public enum AssetValuationMode
{
    /// <summary>Value = <c>Quantity × last PriceQuote × FxRate(quote currency→PLN)</c>. The asset points at a MarketData <c>Instrument</c> (<c>Asset.InstrumentId</c>).</summary>
    Market,

    /// <summary>Value = <c>ManualValueAmount × FxRate(Asset.Currency→PLN)</c> at the snapshot date. No instrument.</summary>
    Manual,

    /// <summary>Value = <c>Quantity × FxRate(Asset.Currency→PLN)</c>. No instrument, no manual amount — the asset's own currency and quantity are the whole story (e.g. plain cash, a term deposit).</summary>
    CurrencyValued,
}

/// <summary>
/// The asset-class → default-valuation-mode mapping (M1.4 scope), read by Portfolio's request
/// validation (which classes may be created with neither <c>InstrumentId</c> nor a manual value) and
/// meant for M1.6 (provider resolution) and M1.7 (the New Asset form) to reuse rather than
/// reimplement. One mapping, not three.
/// </summary>
/// <remarks>
/// This is a <b>default</b>, not a hard constraint: Market (<c>InstrumentId</c>) and Manual
/// (<c>ManualValue</c> + <c>ManualValueDate</c>) stay available to every <see cref="AssetClass"/>
/// exactly as they were before M1.4 — restricting them by class would have broken pre-existing,
/// unmodified tests that create e.g. a manually-valued Cash asset. Only the third combination
/// (neither field set) is gated by this mapping, because that combination was always a validation
/// error before M1.4 existed; extending it to <see cref="SupportsCurrencyValued"/> classes doesn't
/// change behaviour for any other class or any previously-valid combination.
/// </remarks>
public static class AssetValuationModes
{
    /// <summary>The default/suggested mode for a class — Cash/Deposit → currency-valued;
    /// Stock/Etf/Bond/Crypto/PreciousMetal → market; RealEstate/Other → manual.</summary>
    public static AssetValuationMode Default(AssetClass assetClass) => assetClass switch
    {
        AssetClass.Cash or AssetClass.Deposit => AssetValuationMode.CurrencyValued,
        AssetClass.Stock or AssetClass.Etf or AssetClass.Bond or AssetClass.Crypto or AssetClass.PreciousMetal => AssetValuationMode.Market,
        AssetClass.RealEstate or AssetClass.Other => AssetValuationMode.Manual,
        _ => throw new ArgumentOutOfRangeException(nameof(assetClass), assetClass, "Unmapped AssetClass — add it to AssetValuationModes.Default."),
    };

    /// <summary>Whether <paramref name="assetClass"/> may be created/updated with neither
    /// <c>InstrumentId</c> nor a manual value (i.e. currency-valued is its default mode).</summary>
    public static bool SupportsCurrencyValued(AssetClass assetClass) => Default(assetClass) == AssetValuationMode.CurrencyValued;

    /// <summary>Every class <see cref="SupportsCurrencyValued"/> — for error messages naming the accepted set (mirrors <c>SupportedCurrencies.Accepted</c>'s style).</summary>
    public static readonly IReadOnlyList<AssetClass> CurrencyValuedClasses =
        Enum.GetValues<AssetClass>().Where(SupportsCurrencyValued).ToArray();
}
