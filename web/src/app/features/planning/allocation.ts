import { TAX_WRAPPER } from '../accounts/account-type';

export const BUCKET_KIND = {
  Investments: 0,
  Savings: 1,
  Cushion: 2,
  Ike: 3,
  Ikze: 4,
  IkzeJdg: 5,
} as const;
export const SHARE_TYPE = { Fixed: 0, Percent: 1 } as const;

// The cushion is added through its own button, so it is not an option of the kind select.
export const BUCKET_KINDS = [
  { value: BUCKET_KIND.Investments, label: 'planning.allocation.kind.investments' },
  { value: BUCKET_KIND.Savings, label: 'planning.allocation.kind.savings' },
  { value: BUCKET_KIND.Ike, label: 'planning.allocation.kind.ike' },
  { value: BUCKET_KIND.Ikze, label: 'planning.allocation.kind.ikze' },
  { value: BUCKET_KIND.IkzeJdg, label: 'planning.allocation.kind.ikzeJdg' },
] as const;

export const SHARE_TYPES = [
  { value: SHARE_TYPE.Fixed, label: 'planning.allocation.shareType.fixed' },
  { value: SHARE_TYPE.Percent, label: 'planning.allocation.shareType.percent' },
] as const;

export const MIN_TARGET_MONTHS = 1;
export const MAX_TARGET_MONTHS = 24;

export function isLimitedKind(kind: number | string): boolean {
  const value = Number(kind);
  return value === BUCKET_KIND.Ike || value === BUCKET_KIND.Ikze || value === BUCKET_KIND.IkzeJdg;
}

// The account wrapper a limited bucket's account must have; null for a kind without a limit.
export function wrapperForKind(kind: number | string): number | null {
  switch (Number(kind)) {
    case BUCKET_KIND.Ike:
      return TAX_WRAPPER.Ike;
    case BUCKET_KIND.Ikze:
    case BUCKET_KIND.IkzeJdg:
      return TAX_WRAPPER.Ikze;
    default:
      return null;
  }
}

export function bucketKindKey(kind: number | string): string {
  if (Number(kind) === BUCKET_KIND.Cushion) {
    return 'planning.allocation.kind.cushion';
  }

  return (BUCKET_KINDS.find((option) => option.value === Number(kind)) ?? BUCKET_KINDS[0]).label;
}
