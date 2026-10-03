import type { AssetClass } from '../../api/portfolio';

// Mirrors Skarbiec.Contracts.AssetClass in declaration order: the enum travels as its int.
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
  { value: 9, label: 'enums.assetClass.savings' },
];

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
  Savings: 9,
} as const satisfies Record<string, AssetClass>;

export function assetClassLabel(value: AssetClass): string {
  return ASSET_CLASSES.find((c) => c.value === Number(value))?.label ?? 'enums.assetClass.unknown';
}
