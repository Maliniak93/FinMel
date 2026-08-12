import type { AssetClass } from '../../api/portfolio';
import { ASSET_CLASS } from './asset-class';

// Backend enum (Skarbiec.Contracts.AssetValuationMode, M1.4) serializes as its underlying int — same
// declaration order (Market, Manual, CurrencyValued) as contracts/Skarbiec.Contracts/AssetValuationMode.cs.
export const VALUATION_MODE = {
  Market: 0,
  Manual: 1,
  CurrencyValued: 2,
} as const;

export type ValuationModeValue = (typeof VALUATION_MODE)[keyof typeof VALUATION_MODE];

// Mirrors Skarbiec.Contracts.AssetValuationModes.Default (M1.4) exactly — same grouping, same
// fallback. There is no codegen link between the two: AssetValuationMode is a bare `number` on the
// wire and this *mapping* (which class defaults to which mode) never crosses the wire at all, unlike
// SupportedCurrencies (M1.2), which at least has a matching backend type to diff against. The
// anti-drift mechanism here is asset-valuation-mode.spec.ts, which pins every AssetClass to its
// expected mode by name — if AssetValuationModes.Default is ever edited, that test (not a compiler)
// is what forces this switch to be edited to match.
export function defaultValuationMode(assetClass: AssetClass): ValuationModeValue {
  switch (Number(assetClass)) {
    case ASSET_CLASS.Cash:
    case ASSET_CLASS.Deposit:
      return VALUATION_MODE.CurrencyValued;
    case ASSET_CLASS.Stock:
    case ASSET_CLASS.Etf:
    case ASSET_CLASS.Bond:
    case ASSET_CLASS.Crypto:
    case ASSET_CLASS.PreciousMetal:
      return VALUATION_MODE.Market;
    case ASSET_CLASS.RealEstate:
    case ASSET_CLASS.Other:
    default:
      return VALUATION_MODE.Manual;
  }
}

// Mirrors Skarbiec.MarketData.Data.AssetClassPriceSourceMapping.Resolve (M1.6): every Market-mode
// class has a real "add a custom ticker" path except PreciousMetal, whose only provider (NBP) has no
// notion of an arbitrary user-typed ticker — AddCustomInstrumentHandler always rejects it with
// Validation.UnsupportedInstrumentSource. The dialog uses this to hide the "can't find it?" link for
// PreciousMetal instead of offering a path that's guaranteed to 400; the one real gold instrument is
// already seeded Verified and reachable through search.
export function canAddCustomInstrument(assetClass: AssetClass): boolean {
  return (
    defaultValuationMode(assetClass) === VALUATION_MODE.Market &&
    Number(assetClass) !== ASSET_CLASS.PreciousMetal
  );
}
