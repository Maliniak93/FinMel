export const BUCKET_KIND = { Investments: 0, Savings: 1 } as const;
export const SHARE_TYPE = { Fixed: 0, Percent: 1 } as const;

export const BUCKET_KINDS = [
  { value: BUCKET_KIND.Investments, label: 'planning.allocation.kind.investments' },
  { value: BUCKET_KIND.Savings, label: 'planning.allocation.kind.savings' },
] as const;

export const SHARE_TYPES = [
  { value: SHARE_TYPE.Fixed, label: 'planning.allocation.shareType.fixed' },
  { value: SHARE_TYPE.Percent, label: 'planning.allocation.shareType.percent' },
] as const;

export function bucketKindKey(kind: number | string): string {
  return (BUCKET_KINDS.find((option) => option.value === Number(kind)) ?? BUCKET_KINDS[0]).label;
}
