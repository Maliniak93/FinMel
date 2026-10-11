import type { AssetClass } from '../../api/portfolio';

// Mirrors Skarbiec.Contracts.AssetClass: the enum travels as its int.
export const ASSET_CLASSES: readonly { value: AssetClass; label: string }[] = [
  { value: 0, label: 'enums.assetClass.cash' },
  { value: 1, label: 'enums.assetClass.deposit' },
  { value: 2, label: 'enums.assetClass.stock' },
  { value: 3, label: 'enums.assetClass.etf' },
  { value: 4, label: 'enums.assetClass.bond' },
  { value: 6, label: 'enums.assetClass.preciousMetal' },
  { value: 9, label: 'enums.assetClass.savings' },
];

export const ASSET_CLASS = {
  Cash: 0,
  Deposit: 1,
  Stock: 2,
  Etf: 3,
  Bond: 4,
  PreciousMetal: 6,
  Savings: 9,
} as const satisfies Record<string, AssetClass>;

export function assetClassLabel(value: AssetClass): string {
  return ASSET_CLASSES.find((c) => c.value === Number(value))?.label ?? 'enums.assetClass.unknown';
}

const ASSET_CLASS_PAGES: Record<number, string> = {
  [ASSET_CLASS.Cash]: '/cash',
  [ASSET_CLASS.Deposit]: '/deposits',
  [ASSET_CLASS.Stock]: '/securities',
  [ASSET_CLASS.Etf]: '/securities',
  [ASSET_CLASS.Bond]: '/bonds',
  [ASSET_CLASS.PreciousMetal]: '/metals',
  [ASSET_CLASS.Savings]: '/deposits',
};

export function assetClassPage(value: AssetClass): string {
  return ASSET_CLASS_PAGES[Number(value)] ?? '/dashboard';
}
