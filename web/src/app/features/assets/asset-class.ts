import type { AssetClass } from '../../api/portfolio';

// Backend enum (Skarbiec.Contracts.AssetClass) serializes as its underlying int, so the generated
// client types it as a bare `number` — labels for the UI have to be maintained here, in the same
// declaration order as the C# enum. A label is a translation key: templates render it through the
// `transloco` pipe.
export const ASSET_CLASSES: readonly { value: AssetClass; label: string }[] = [
  { value: 0, label: 'enums.assetClass.cash' },
  { value: 1, label: 'enums.assetClass.deposit' },
  { value: 2, label: 'enums.assetClass.stock' },
  { value: 3, label: 'enums.assetClass.etf' },
  { value: 4, label: 'enums.assetClass.bond' },
  { value: 5, label: 'enums.assetClass.crypto' },
  { value: 6, label: 'enums.assetClass.preciousMetal' },
  { value: 7, label: 'enums.assetClass.realEstate' },
  { value: 8, label: 'enums.assetClass.other' },
];

// Named constants for the same values, so callers that branch on a specific class (the asset-form
// shell's class-to-form mapping) read as intent ("PreciousMetal") rather than a bare int.
export const ASSET_CLASS = {
  Cash: 0,
  Deposit: 1,
  Stock: 2,
  Etf: 3,
  Bond: 4,
  Crypto: 5,
  PreciousMetal: 6,
  RealEstate: 7,
  Other: 8,
} as const satisfies Record<string, AssetClass>;

// A translation key.
export function assetClassLabel(value: AssetClass): string {
  return ASSET_CLASSES.find((c) => c.value === Number(value))?.label ?? 'enums.assetClass.unknown';
}
