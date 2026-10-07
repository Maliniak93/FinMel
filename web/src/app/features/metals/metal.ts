import type { Metal } from '../../api/portfolio';

// Mirrors Skarbiec.Contracts.Metal in declaration order: the enum travels as its int.
export const METAL = { Gold: 0, Silver: 1 } as const;

export const METALS: readonly { value: Metal; label: string }[] = [
  { value: METAL.Gold, label: 'metals.metal.gold' },
  { value: METAL.Silver, label: 'metals.metal.silver' },
];

export const GRAMS_PER_TROY_OUNCE = 31.1034768;

const WHOLE_OUNCE_TOLERANCE_GRAMS = 0.01;

export function metalLabel(value: Metal): string {
  return METALS.find((metal) => metal.value === Number(value))?.label ?? 'metals.metal.unknown';
}

export function wholeTroyOunces(grams: number | string): number | null {
  const ounces = Math.round(Number(grams) / GRAMS_PER_TROY_OUNCE);
  return ounces >= 1 &&
    Math.abs(Number(grams) - ounces * GRAMS_PER_TROY_OUNCE) <= WHOLE_OUNCE_TOLERANCE_GRAMS
    ? ounces
    : null;
}
