import type { AssetClass } from '../../api/portfolio';

// Backend enum (Skarbiec.Contracts.AssetClass) serializes as its underlying int, so the generated
// client types it as a bare `number` — labels for the UI have to be maintained here, in the same
// declaration order as the C# enum.
export const ASSET_CLASSES: readonly { value: AssetClass; label: string }[] = [
  { value: 0, label: 'Cash' },
  { value: 1, label: 'Deposit' },
  { value: 2, label: 'Stock' },
  { value: 3, label: 'ETF' },
  { value: 4, label: 'Bond' },
  { value: 5, label: 'Crypto' },
  { value: 6, label: 'Precious metal' },
  { value: 7, label: 'Real estate' },
  { value: 8, label: 'Other' },
];

export function assetClassLabel(value: AssetClass): string {
  return ASSET_CLASSES.find((c) => c.value === Number(value))?.label ?? 'Unknown';
}
