// Backend enum (Skarbiec.Contracts.AssetValuationMode, M1.4) serializes as its underlying int — same
// declaration order (Market, Manual, CurrencyValued) as contracts/Skarbiec.Contracts/AssetValuationMode.cs.
export const VALUATION_MODE = {
  Market: 0,
  Manual: 1,
  CurrencyValued: 2,
} as const;
