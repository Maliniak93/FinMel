using Skarbiec.Contracts;

namespace Skarbiec.MarketData.Data;

/// <summary>
/// Which <see cref="PriceSource"/> can verify/price a custom, user-typed ticker for a given
/// <see cref="AssetClass"/> (M1.6 scope: "adding an ETF should only check Stooq, not crypto").
/// Downstream of, not a duplicate of, <c>Skarbiec.Contracts.AssetValuationModes.Default</c> (M1.4) —
/// that mapping says which <see cref="AssetClass"/>es are priced by a market instrument at all; this
/// one says which concrete provider prices it, which <c>AssetValuationModes</c> can't express because
/// <see cref="PriceSource"/> is a MarketData-owned enum, not a Contracts one (mirrors
/// <see cref="Instrument.Source"/>/<c>MarketDataSeeder</c>'s own class-&gt;source choices).
/// </summary>
/// <remarks>
/// <b>PreciousMetal → NBP is intentional, not an oversight.</b> NBP has exactly one priced instrument
/// (gold, fixed ticker <c>XAU</c>, no notion of an arbitrary user-supplied ticker — see
/// <c>Skarbiec.MarketData.Sources.Nbp.NbpPriceSource</c>'s doc comment), so a *custom* PreciousMetal
/// ticker can never be verified: <c>Features.AddCustomInstrument.AddCustomInstrumentHandler</c> keeps
/// rejecting <see cref="PriceSource.Nbp"/> for arbitrary tickers exactly as it did before M1.6 (the
/// existing "NBP only serves its fixed endpoints" error) — this mapping is what now feeds that check
/// instead of a user-supplied <c>Source</c> field. The one real gold instrument
/// (<c>MarketDataSeeder</c>'s <c>XAU</c>) is already seeded <see cref="InstrumentVerificationStatus.Verified"/>
/// and reachable through <c>SearchInstruments</c>, so PreciousMetal simply has no "add a custom one"
/// path — a user picks gold from the dictionary rather than typing a ticker for it.
/// </remarks>
public static class AssetClassPriceSourceMapping
{
    /// <summary>The provider that prices <paramref name="assetClass"/>, or <c>null</c> when the class
    /// has no market provider at all — Cash/Deposit/RealEstate/Other are currency-valued or manually
    /// valued by default (<c>AssetValuationModes.Default</c>) and never point at a MarketData
    /// <see cref="Instrument"/>, so there is nothing for a custom-instrument endpoint to verify.</summary>
    public static PriceSource? Resolve(AssetClass assetClass) => assetClass switch
    {
        AssetClass.Stock or AssetClass.Etf or AssetClass.Bond => PriceSource.Stooq,
        AssetClass.Crypto => PriceSource.CoinGecko,
        AssetClass.PreciousMetal => PriceSource.Nbp,
        AssetClass.Cash or AssetClass.Deposit or AssetClass.RealEstate or AssetClass.Other => null,
        _ => throw new ArgumentOutOfRangeException(nameof(assetClass), assetClass, "Unmapped AssetClass — add it to AssetClassPriceSourceMapping.Resolve."),
    };
}
