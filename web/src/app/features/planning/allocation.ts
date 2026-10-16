export const BUCKET_KIND = { Investments: 0, Savings: 1, Cushion: 2 } as const;
export const SHARE_TYPE = { Fixed: 0, Percent: 1 } as const;

// The cushion is added through its own button, so it is not an option of the kind select.
export const BUCKET_KINDS = [
  { value: BUCKET_KIND.Investments, label: 'planning.allocation.kind.investments' },
  { value: BUCKET_KIND.Savings, label: 'planning.allocation.kind.savings' },
] as const;

export const SHARE_TYPES = [
  { value: SHARE_TYPE.Fixed, label: 'planning.allocation.shareType.fixed' },
  { value: SHARE_TYPE.Percent, label: 'planning.allocation.shareType.percent' },
] as const;

export const MIN_TARGET_MONTHS = 1;
export const MAX_TARGET_MONTHS = 24;

export function bucketKindKey(kind: number | string): string {
  if (Number(kind) === BUCKET_KIND.Cushion) {
    return 'planning.allocation.kind.cushion';
  }

  return (BUCKET_KINDS.find((option) => option.value === Number(kind)) ?? BUCKET_KINDS[0]).label;
}
