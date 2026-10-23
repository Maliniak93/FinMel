import { ASSET_CLASS } from '../features/assets/asset-class';

// Mirrors Skarbiec.Contracts.AssetGroupColors.Keys.
export const GROUP_COLOR_KEYS: readonly string[] = [
  'blue',
  'green',
  'orange',
  'purple',
  'red',
  'teal',
  'pink',
  'amber',
  'indigo',
  'lime',
  'cyan',
  'brown',
];

const NEUTRAL = 'var(--sk-g-none)';

const ASSET_CLASS_TOKENS: Record<number, string> = {
  [ASSET_CLASS.Cash]: 'cash',
  [ASSET_CLASS.Deposit]: 'deposit',
  [ASSET_CLASS.Stock]: 'stock',
  [ASSET_CLASS.Etf]: 'etf',
  [ASSET_CLASS.Bond]: 'bond',
  [ASSET_CLASS.PreciousMetal]: 'metal',
  [ASSET_CLASS.BankAccount]: 'bank',
};

export function groupColor(key: string | null | undefined): string {
  return key !== null && key !== undefined && GROUP_COLOR_KEYS.includes(key)
    ? `var(--sk-g-${key})`
    : NEUTRAL;
}

export function assetClassColor(assetClass: number): string {
  const token = ASSET_CLASS_TOKENS[Number(assetClass)];
  return token === undefined ? NEUTRAL : `var(--sk-c-${token})`;
}
